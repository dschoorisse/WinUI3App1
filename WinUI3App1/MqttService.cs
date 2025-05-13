using System;
using System.Collections.Generic; // Added for MqttUserProperty if needed later
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;
using Serilog;

namespace WinUI3App1
{
    public class MqttService : IDisposable
    {
        private readonly ILogger _logger;
        private IMqttClient? _mqttClient;
        private readonly MqttClientFactory _mqttFactory;
        private readonly MqttClientOptions _mqttClientOptions;
        private Timer? _reconnectTimer;
        private bool _isDisposed;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly string _photoboothId; // Store the ID

        // Remote settings updates
        public event EventHandler SettingsUpdatedRemotely;
        private string _remoteSettingsSetTopic = ""; // Will be initialized with PhotoboothIdentifier

        public event EventHandler<bool>? ConnectionStatusChanged;

        public bool IsConnected => _mqttClient?.IsConnected ?? false;

        public MqttService(ILogger logger, string photoboothId, string brokerAddress, int port, string? username, string? password)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _photoboothId = photoboothId ?? throw new ArgumentNullException(nameof(photoboothId));
            _mqttFactory = new MqttClientFactory();

            if (string.IsNullOrWhiteSpace(brokerAddress)) { /*...*/ }
            if (string.IsNullOrWhiteSpace(_photoboothId)) { /*...*/ }

            // Create settings topic to monitor
            _remoteSettingsSetTopic = $"photobooth/{_photoboothId}/settings/apply";

            // --- Definieer LWT Eigenschappen ---
            string lastWillTopic = $"photobooth/{_photoboothId}/connection";
            string lastWillPayload = "disconnected"; // Payload voor LWT
            var lastWillQoS = MqttQualityOfServiceLevel.AtLeastOnce; // QoS voor LWT
            bool lastWillRetain = true; // Retain flag voor LWT
            // ----------------------------------

            string uniqueClientId = $"photobooth_{_photoboothId}_{Guid.NewGuid().ToString().Substring(0, 8)}";

            // --- Builder Correctie ---
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(brokerAddress, port)
                .WithClientId(uniqueClientId)
                .WithCleanSession() // Overweeg false voor betrouwbaardere QoS 1/2 LWT levering
                .WithTimeout(TimeSpan.FromSeconds(15))
                // --- Stel LWT eigenschappen individueel in ---
                .WithWillTopic(lastWillTopic)
                .WithWillPayload(Encoding.UTF8.GetBytes(lastWillPayload)) // Payload als byte array
                .WithWillQualityOfServiceLevel(lastWillQoS)
                .WithWillRetain(lastWillRetain);
            // --- Einde LWT Configuratie ---

            if (!string.IsNullOrEmpty(username))
            {
                builder = builder.WithCredentials(username, password);
                _logger.Information("MQTT [{PhotoboothId}]: Using username for authentication.", _photoboothId);
            }
            else
            {
                _logger.Information("MQTT [{PhotoboothId}]: Connecting without username/password.", _photoboothId);
            }

            _mqttClientOptions = builder.Build(); // Bouw de opties na alle configuratie
            _logger.Information("MQTT [{PhotoboothId}] Service initialized for broker {BrokerAddress}:{Port} with LWT on topic '{LwtTopic}'",
               _photoboothId, brokerAddress, port, lastWillTopic);
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.Information("MQTT Service starting...");
            _mqttClient = _mqttFactory.CreateMqttClient();

            _mqttClient.ConnectedAsync += HandleConnectedAsync;
            _mqttClient.DisconnectedAsync += HandleDisconnectedAsync;
            _mqttClient.ApplicationMessageReceivedAsync += HandleApplicationMessageReceivedAsync;

            await ConnectWithRetryAsync(cancellationToken);
        }

        private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
        {
            if (_isDisposed || cancellationToken.IsCancellationRequested || _mqttClient == null) return;

            _logger.Information("MQTT [{PhotoboothId}]: Attempting to connect...", _photoboothId); // Log met ID
            try
            {
                if (_mqttClient.IsConnected) { /*...*/ return; }

                var connectResult = await _mqttClient.ConnectAsync(_mqttClientOptions, cancellationToken);

                if (connectResult.ResultCode == MqttClientConnectResultCode.Success)
                {
                    _logger.Information("MQTT [{PhotoboothId}]: Successfully connected.", _photoboothId);
                    _reconnectTimer?.Dispose();
                    _reconnectTimer = null;
                }
                else
                {
                    _logger.Error("MQTT [{PhotoboothId}]: Failed to connect. Reason: {Reason}", _photoboothId, connectResult.ReasonString ?? connectResult.ResultCode.ToString());
                    ScheduleReconnect(cancellationToken);
                }
            }
            catch (MqttCommunicationException ex)
            {
                _logger.Error(ex, "MQTT: Communication error while connecting.");
                ScheduleReconnect(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.Warning("MQTT: Connection attempt cancelled by external token.");
            }
            catch (OperationCanceledException)
            {
                // This might happen if the internal token is cancelled during connect
                _logger.Warning("MQTT: Connection attempt cancelled (likely during dispose).");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT: An unexpected error occurred during connection attempt.");
                ScheduleReconnect(cancellationToken);
            }
            finally
            {
                // Update status regardless of outcome, unless disposed
                if (!_isDisposed)
                {
                    OnConnectionStatusChanged(IsConnected);
                }
            }
        }

        private async Task HandleConnectedAsync(MqttClientConnectedEventArgs e)
        {
            _logger.Information("MQTT [{PhotoboothId}]: Connected event received.", _photoboothId);
            OnConnectionStatusChanged(true);

            // Subscribe to settings command update
            _logger.Information($"Subscribing to {_remoteSettingsSetTopic}");
            await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(_remoteSettingsSetTopic).Build());

            // Example: Subscribe to a topic specific to this booth
            // _ = SubscribeToTopicAsync($"photobooth/{_photoboothId}/commands");
        }

        private async Task HandleDisconnectedAsync(MqttClientDisconnectedEventArgs e)
        {
            // Check if the service is already disposed to avoid logging/reconnecting after explicit cleanup
            if (_isDisposed)
            {
                _logger.Information("MQTT: Disconnected event received during or after dispose. Reason: {Reason}.", e.Reason);
                OnConnectionStatusChanged(false); // Ensure status is updated
                return; // Exit early if disposed
            }

            _logger.Warning("MQTT [{PhotoboothId}]: Disconnected. Reason: {Reason}. ClientWasConnected: {ClientWasConnected}. Reconnecting...", _photoboothId, e.Reason, e.ClientWasConnected); 
            OnConnectionStatusChanged(false);

            // Only attempt reconnect if disconnection was not initiated by Dispose or a clean disconnect command
            if (e.Reason != MqttClientDisconnectReason.NormalDisconnection)
            {
                // Wait a short period before attempting to reconnect
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _cancellationTokenSource.Token);
                    // Pass the internal cancellation token to the connect attempt
                    await ConnectWithRetryAsync(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.Information("MQTT: Reconnect delay or attempt cancelled (likely during dispose).");
                }
            }
            else
            {
                _logger.Information("MQTT: Disconnection was intended or service is disposing. No reconnect attempt.");
            }
        }

        private async Task HandleApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            // Log met ID
            _logger.Information("MQTT [{PhotoboothId}]: Received on topic '{Topic}'. Payload: {Payload}", _photoboothId, e.ApplicationMessage.Topic, Encoding.UTF8.GetString(e.ApplicationMessage.Payload));
                        string topic = e.ApplicationMessage.Topic;
            string payload = System.Text.Encoding.UTF8.GetString(e.ApplicationMessage.Payload);

            if (topic == _remoteSettingsSetTopic)
            {
                _logger.Information("MQTT [{PhotoboothId}]: Received settings update on topic '{Topic}'.", _photoboothId, topic);
                await ProcessRemoteSettingsUpdateAsync(payload);
            }

            // Process message here
        }

        private async Task ProcessRemoteSettingsUpdateAsync(string jsonPayload)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var remoteSettings = JsonSerializer.Deserialize<PhotoBoothSettings>(jsonPayload, options);

                if (remoteSettings == null)
                {
                    _logger.Error("MQTT [{PhotoboothId}]: Failed to deserialize remote settings payload.", _photoboothId);
                    return;
                }
                // Ensure remoteSettings has a valid timestamp; if not, could ignore or assign DateTime.MinValue
                // but our model constructor should give it one if the JSON field was missing.

                _logger.Information("MQTT [{PhotoboothId}]: Deserialized remote settings. Remote Timestamp: {RemoteTimestamp}", _photoboothId, remoteSettings.LastModifiedUtc);

                PhotoBoothSettings localSettings = await SettingsManager.LoadSettingsAsync();
                if (localSettings == null)
                {
                    _logger.Error("MQTT [{PhotoboothId}]: Critical - Failed to load local settings for comparison.", _photoboothId);
                    return; // Or handle more gracefully
                }
                _logger.Information("MQTT [{PhotoboothId}]: Loaded local settings for comparison. Local Timestamp: {LocalTimestamp}", _photoboothId, localSettings.LastModifiedUtc);


                if (remoteSettings.LastModifiedUtc > localSettings.LastModifiedUtc)
                {
                    _logger.Information("MQTT [{PhotoboothId}]: Remote settings are newer. Applying and saving.", _photoboothId);
                    await SettingsManager.SaveSettingsAsync(remoteSettings, true); // true to preserve remote timestamp

                    SettingsUpdatedRemotely?.Invoke(this, EventArgs.Empty); // Notify app
                }
                else
                {
                    _logger.Information("MQTT [{PhotoboothId}]: Remote settings (Timestamp: {RemoteTimestamp}) are not newer than local (Timestamp: {LocalTimestamp}). No update applied.",
                        _photoboothId, remoteSettings.LastModifiedUtc, localSettings.LastModifiedUtc);
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.Error(jsonEx, "MQTT [{PhotoboothId}]: JSON Deserialization error for remote settings.", _photoboothId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT [{PhotoboothId}]: Error processing remote settings update.", _photoboothId);
            }
        }

        public async Task PublishAsync(string topic, string payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce, bool retain = false)
        {
            if (_mqttClient == null || !_mqttClient.IsConnected || _isDisposed)
            {
                _logger.Warning("MQTT: Cannot publish message, client is not connected or disposed.");
                return;
            }

            var applicationMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(qos)
                .WithRetainFlag(retain)
                .Build();

            try
            {
                var result = await _mqttClient.PublishAsync(applicationMessage, _cancellationTokenSource.Token);
                if (result.ReasonCode == MqttClientPublishReasonCode.Success)
                {
                    _logger.Information("MQTT: Message published successfully to topic '{Topic}'.", topic);
                }
                else
                {
                    _logger.Error("MQTT: Failed to publish message to topic '{Topic}'. Reason: {Reason}", topic, result.ReasonCode);
                }
            }
            catch (MqttCommunicationException ex)
            {
                _logger.Error(ex, "MQTT: Communication error while publishing to topic '{Topic}'.", topic);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("MQTT: Publish operation cancelled for topic '{Topic}'.", topic);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT: An unexpected error occurred while publishing to topic '{Topic}'.", topic);
            }
        }

        public async Task SubscribeToTopicAsync(string topic, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce)
        {
            _logger.Information($"Subscribing to topic {topic}");

            if (_mqttClient == null || !_mqttClient.IsConnected || _isDisposed)
            {
                _logger.Warning("MQTT: Cannot subscribe, client is not connected or disposed.");
                return;
            }

            var topicFilter = new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(qos)
                .Build();

            try
            {
                var subscribeResult = await _mqttClient.SubscribeAsync(topicFilter, _cancellationTokenSource.Token);

                foreach (var subscription in subscribeResult.Items)
                {
                    if (subscription.ResultCode == MqttClientSubscribeResultCode.GrantedQoS0 ||
                        subscription.ResultCode == MqttClientSubscribeResultCode.GrantedQoS1 ||
                        subscription.ResultCode == MqttClientSubscribeResultCode.GrantedQoS2)
                    {
                        _logger.Information("MQTT: Successfully subscribed to topic '{Topic}' with QoS {QoS}.", subscription.TopicFilter.Topic, subscription.ResultCode);
                    }
                    else
                    {
                        _logger.Error("MQTT: Failed to subscribe to topic '{Topic}'. Reason: {Reason}", subscription.TopicFilter.Topic, subscription.ResultCode);
                    }
                }
            }
            catch (MqttCommunicationException ex)
            {
                _logger.Error(ex, "MQTT: Communication error while subscribing to topic '{Topic}'.", topic);
            }
            catch (OperationCanceledException)
            {
                _logger.Warning("MQTT: Subscribe operation cancelled for topic '{Topic}'.", topic);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "MQTT: An unexpected error occurred while subscribing to topic '{Topic}'.", topic);
            }
        }

        private void ScheduleReconnect(CancellationToken cancellationToken)
        {
            if (_isDisposed || cancellationToken.IsCancellationRequested) return;

            if (_reconnectTimer == null)
            {
                _logger.Information("MQTT [{PhotoboothId}]: Scheduling reconnect.", _photoboothId);
                // Use the internal cancellation token for the timer callback
                _reconnectTimer = new Timer(async _ =>
                {
                    // Check if disposed before attempting reconnect inside timer
                    if (!_isDisposed && !_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await ConnectWithRetryAsync(_cancellationTokenSource.Token);
                    }
                    else
                    {
                        _logger.Information("MQTT: Reconnect timer fired but service is disposed or cancelled.");
                    }
                }, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan); // Pass null state, handle cancellation within callback
            }
            else
            {
                _logger.Debug("MQTT: Reconnect timer already scheduled."); // Changed to Debug level
            }
        }

        protected virtual void OnConnectionStatusChanged(bool isConnected)
        {
            // Avoid invoking events if disposed
            if (!_isDisposed)
            {
                ConnectionStatusChanged?.Invoke(this, isConnected);
                _logger.Information("MQTT [{PhotoboothId}]: Connection status changed: {IsConnected}", _photoboothId, isConnected);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_isDisposed) return;
            _isDisposed = true; // Zet flag vroeg
            _logger.Information("MQTT [{PhotoboothId}]: Service disposing...", _photoboothId);

            // Annuleer lopende operaties (zoals reconnect pogingen)
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                try { _cancellationTokenSource.Cancel(); } catch (ObjectDisposedException) { /* Ignore if already disposed */ }
            }

            // Dispose timer veilig
            // Gebruik try-catch voor het geval de timer al gedisposed is op een andere thread
            try { _reconnectTimer?.Dispose(); } catch (ObjectDisposedException) { /* Ignore */ }
            _reconnectTimer = null; // Zekerstellen dat timer null is

            if (_mqttClient != null)
            {
                // Koppel events los om callbacks na dispose te voorkomen
                try
                {
                    _mqttClient.ConnectedAsync -= HandleConnectedAsync;
                    _mqttClient.DisconnectedAsync -= HandleDisconnectedAsync;
                    _mqttClient.ApplicationMessageReceivedAsync -= HandleApplicationMessageReceivedAsync;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "MQTT [{PhotoboothId}]: Exception during event unsubscribe.", _photoboothId);
                }


                // Alleen proberen te publiceren/disconnecten als de client nog bestond en mogelijk verbonden was
                bool wasConnected = _mqttClient.IsConnected; // Check status voor de poging

                if (wasConnected)
                {
                    try
                    {
                        // --- PUBLISH EXPLICIETE "OFFLINE_EXPECTED" STATUS ---
                        string offlineTopic = $"photobooth/{_photoboothId}/connection";
                        string offlinePayload = "shutdown"; // Payload voor normale shutdown
                        var offlineMessage = new MqttApplicationMessageBuilder()
                            .WithTopic(offlineTopic)
                            .WithPayload(offlinePayload)
                            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce) // QoS 1
                            .WithRetainFlag(true) // Behoud de offline status
                            .Build();

                        _logger.Information("MQTT [{PhotoboothId}]: Publishing explicit '{Payload}' status before disconnecting...", _photoboothId, offlinePayload);
                        // Probeer te publiceren met een korte timeout
                        using var pubCts = new CancellationTokenSource(TimeSpan.FromSeconds(2)); // 2 seconden timeout
                        var pubResult = await _mqttClient.PublishAsync(offlineMessage, pubCts.Token);
                        if (pubResult.ReasonCode == MqttClientPublishReasonCode.Success)
                        {
                            _logger.Information("MQTT [{PhotoboothId}]: Explicit '{Payload}' status published successfully.", _photoboothId, offlinePayload);
                        }
                        else
                        {
                            // Log als waarschuwing, we gaan toch door met disconnecten
                            _logger.Warning("MQTT [{PhotoboothId}]: Failed to publish explicit '{Payload}' status. Reason: {Reason}", _photoboothId, offlinePayload, pubResult.ReasonCode);
                        }

                        // Geef het bericht een klein momentje om te verzenden
                        try { await Task.Delay(100, _cancellationTokenSource.Token); } catch (OperationCanceledException) { /* Ignore cancellation during delay */ }

                        // --------------------------------------------------

                        // --- VOER NU PAS DE NETTE DISCONNECT UIT ---
                        _logger.Information("MQTT [{PhotoboothId}]: Performing clean disconnect...", _photoboothId);
                        var disconnectOptions = _mqttFactory.CreateClientDisconnectOptionsBuilder()
                           .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                           .Build();
                        // Gebruik een aparte CancellationToken voor de disconnect zelf
                        using var discCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); // 3 seconden timeout
                        await _mqttClient.DisconnectAsync(disconnectOptions, discCts.Token);
                        _logger.Information("MQTT [{PhotoboothId}]: Cleanly disconnected.", _photoboothId);
                        // -----------------------------------------
                    }
                    catch (OperationCanceledException ex)
                    {
                        // Dit kan gebeuren als de publish of disconnect timeout
                        _logger.Warning(ex, "MQTT [{PhotoboothId}]: Final publish or disconnect operation timed out or was cancelled during dispose.", _photoboothId);
                    }
                    catch (Exception ex)
                    {
                        // Log de fout, maar ga door met disposen
                        _logger.Error(ex, "MQTT [{PhotoboothId}]: Error during final publish or disconnect on dispose.", _photoboothId);
                    }
                }
                else
                {
                    // Log dat we niet verbonden waren, dus ook niet publiceren/disconnecten
                    _logger.Information("MQTT [{PhotoboothId}]: Client was not connected during dispose, skipping final publish/disconnect.", _photoboothId);
                }

                // Dispose de client zelf, ongeacht of disconnect lukte
                try { _mqttClient.Dispose(); } catch (Exception ex) { _logger.Warning(ex, "MQTT [{PhotoboothId}]: Exception during MqttClient dispose.", _photoboothId); }
                _mqttClient = null; // Goede praktijk om referentie te nullen na dispose
            }
            else
            {
                _logger.Information("MQTT [{PhotoboothId}]: MQTT client was already null during dispose.", _photoboothId);
            }


            // Dispose de cancellation token source
            try { _cancellationTokenSource.Dispose(); } catch (Exception ex) { _logger.Warning(ex, "MQTT [{PhotoboothId}]: Exception during CancellationTokenSource dispose.", _photoboothId); }


            _logger.Information("MQTT [{PhotoboothId}]: Service disposed.", _photoboothId);
        }

        // Standard Dispose pattern implementation
        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
            GC.SuppressFinalize(this);
        }

        ~MqttService()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}