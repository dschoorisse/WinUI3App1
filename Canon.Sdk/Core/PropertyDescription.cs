using System;

namespace Canon.Sdk.Core
{
    /// <summary>
    /// Contains the description of a property and its possible values.
    /// </summary>
    public class PropertyDescription
    {
        /// <summary>
        /// Gets the form of the property (how it is stored).
        /// </summary>
        public int Form { get; }

        /// <summary>
        /// Gets the access rights for the property.
        /// </summary>
        public uint Access { get; }

        /// <summary>
        /// Gets the number of elements in the property description.
        /// </summary>
        public int NumElements { get; }

        /// <summary>
        /// Gets the possible values for the property.
        /// </summary>
        public int[] Values { get; }

        /// <summary>
        /// Initializes a new instance of the PropertyDescription class.
        /// </summary>
        /// <param name="propertyDesc">The native property description structure.</param>
        internal PropertyDescription(EDSDKLib.EDSDK.EdsPropertyDesc propertyDesc)
        {
            Form = propertyDesc.Form;
            Access = propertyDesc.Access;
            NumElements = propertyDesc.NumElements;

            // Copy property description values
            Values = new int[propertyDesc.NumElements];
            if (propertyDesc.NumElements > 0 && propertyDesc.NumElements <= propertyDesc.PropDesc.Length)
            {
                Array.Copy(propertyDesc.PropDesc, Values, propertyDesc.NumElements);
            }
        }
    }
}