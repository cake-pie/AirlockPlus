using System;
using System.Linq;
using System.Reflection;

namespace AirlockPlus
{
	class CLSClient
	{
		private static ConnectedLivingSpace.ICLSAddon _CLS = null;
		private static bool? _CLSAvailable = null;

		public static ConnectedLivingSpace.ICLSAddon GetCLS() {
			Type CLSAddonType = AssemblyLoader.loadedAssemblies.SelectMany(a => a.assembly.GetExportedTypes()).SingleOrDefault(t => t.FullName == "ConnectedLivingSpace.CLSAddon");
			if (CLSAddonType != null) {
				object realCLSAddon = CLSAddonType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
				_CLS =   (ConnectedLivingSpace.ICLSAddon)realCLSAddon;
			}
			return _CLS;
		}

		public static bool CLSInstalled {
			get {
				if (_CLSAvailable == null)
					_CLSAvailable = GetCLS() != null;
				return (bool)_CLSAvailable;
			}
		}
	}
}
