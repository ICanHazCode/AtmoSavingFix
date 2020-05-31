using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Unity.Collections;

namespace AtmoSavingFix
{
	[BepInPlugin("net.icanhazcode.stationeers.atmosavingfix", "AtmoSavingFix", "0.1.0.0")]
	public class Patch : BaseUnityPlugin
	{
		public static Patch Instance;

		public static bool Debug = false;

		public void Log(string line)
		{
			Logger.LogInfo(line);
		}
		public void LogError(string line)
		{
			Logger.LogError(line);
		}

		Patch()
		{
			Instance = this;
			Debug = Config.Bind(new ConfigDefinition("Logging", "Debug"),
								false,
								new ConfigDescription("Logs transpiler fixes and resultant code")).Value;
		}

		void Awake()
		{
			Log("Patching XmlSaveLoad.GetWorldData");
			try
			{
				var harmony = new Harmony("net.icanhazcode.stationeers.atmosavingfix");
				harmony.PatchAll();
			}
			catch (Exception ex)
			{
				LogError($"Exception thrown:\n{ex.Message}");
				throw ex;
			}
			Log("Patch succeeded.");
		}

	}
}
