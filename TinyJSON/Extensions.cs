using System;

namespace TinyJSON
{
	// MelonLoader used to supply this helper alongside its bundled copy of TinyJSON. TinyJSON is
	// vendored into the mod now that MelonLoader no longer ships it, so the helper comes along too.
	internal static class TinyJSONExtensions
	{
		public static bool AnyOfType( this object[] attributes, Type type )
		{
			if (attributes == null)
			{
				return false;
			}
			foreach (object attribute in attributes)
			{
				if (attribute != null && type.IsAssignableFrom( attribute.GetType() ))
				{
					return true;
				}
			}
			return false;
		}
	}
}
