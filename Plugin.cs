using BepInEx;
using HarmonyLib;
using HG;
using RoR2;
using System.Collections.Generic;
using System.Reflection;
using System.Security.Permissions;

[assembly: AssemblyVersion(Local.Fix.History.Plugin.versionNumber)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
		// Allow private member access via publicized assemblies.

namespace Local.Fix.History
{
	[BepInPlugin("local.fix.history", "HistoryFix", versionNumber)]
	public class Plugin : BaseUnityPlugin
	{
		public const string versionNumber = "0.1.0";
		private static uint historyLimit;

		public void Awake()
		{
			historyLimit = Config.Bind(
					section: "General",
					key: "History Limit",
					defaultValue: 60u,
					description: "Maximum number of run history entries."
				).Value;

			Harmony.CreateAndPatchAll(typeof(Plugin));
		}

		[HarmonyPatch(typeof(MorgueManager), nameof(MorgueManager.EnforceHistoryLimit))]
		[HarmonyPrefix]
		private static bool FixHistoryLimit()
		{
			var historyFiles = CollectionPool<MorgueManager.HistoryFileInfo, 
					List<MorgueManager.HistoryFileInfo>>.RentCollection();
			MorgueManager.GetHistoryFiles(historyFiles);

			for ( int count = historyFiles.Count; count >= historyLimit; --count )
				MorgueManager.RemoveOldestHistoryFile();

			CollectionPool<MorgueManager.HistoryFileInfo, 
					List<MorgueManager.HistoryFileInfo>>.ReturnCollection(historyFiles);
			return false;
		}
	}
}
