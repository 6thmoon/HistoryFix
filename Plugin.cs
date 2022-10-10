using BepInEx;
using HarmonyLib;
using HG;
using RoR2;
using RoR2.Stats;
using RoR2.UI;
using System;
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
		public const string versionNumber = "0.3.0";
		private static uint historyLimit;

		public void Awake()
		{
			historyLimit = Config.Bind(
					section: "General",
					key: "History Limit",
					defaultValue: 120u,
					description: "Maximum number of run reports - set to zero for unlimited "
						 + "entries. Note that extreme values may break certain UI elements."
				).Value;

			Harmony.CreateAndPatchAll(typeof(Plugin));
		}

		[HarmonyPatch(typeof(MorgueManager), nameof(MorgueManager.EnforceHistoryLimit))]
		[HarmonyPrefix]
		private static bool FixHistoryLimit()
		{
			if ( historyLimit > 0 )
			{
				var historyFiles = CollectionPool<MorgueManager.HistoryFileInfo,
						List<MorgueManager.HistoryFileInfo>>.RentCollection();
				MorgueManager.GetHistoryFiles(historyFiles);

				for ( int count = historyFiles.Count; count >= historyLimit; --count )
					MorgueManager.RemoveOldestHistoryFile();

				CollectionPool<MorgueManager.HistoryFileInfo,
						List<MorgueManager.HistoryFileInfo>>.ReturnCollection(historyFiles);
			}
			return false;
		}

		[HarmonyPatch(typeof(StatManager), nameof(StatManager.OnServerGameOver))]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> FixEclipseVictories(
				IEnumerable<CodeInstruction> instructionList)
		{
			MethodInfo getType = typeof(object).GetMethod(nameof(object.GetType));
			foreach ( CodeInstruction instruction in instructionList )
			{
				yield return instruction;

				if ( instruction.Calls(getType) )
				{
					yield return Transpilers.EmitDelegate<Func<Type, Type>>(
							type => type == typeof(EclipseRun) ? typeof(Run) : type
						);
				}
			}
		}

		private static ItemInventoryDisplay inventory = null;
		private static readonly List<EquipmentDef> equipments = new List<EquipmentDef>();

		[HarmonyPatch(typeof(GameEndReportPanelController),
				nameof(GameEndReportPanelController.SetPlayerInfo))]
		[HarmonyPrefix]
		private static void GetEquipment(GameEndReportPanelController __instance,
				RunReport.PlayerInfo playerInfo)
		{
			inventory = __instance.itemInventoryDisplay;
			equipments.Clear();

			foreach ( EquipmentIndex index in playerInfo.equipment )
			{
				EquipmentDef equipment = EquipmentCatalog.GetEquipmentDef(index);
				if ( equipment != null )
					equipments.Add(equipment);
			}
		}

		[HarmonyPatch(typeof(ItemInventoryDisplay), nameof(ItemInventoryDisplay.UpdateDisplay))]
		[HarmonyPostfix]
		private static void AddEquipment(ItemInventoryDisplay __instance)
		{
			if ( inventory == null || inventory != __instance ) return;

			int itemCount = __instance.itemIcons.Count + equipments.Count;
			inventory.AllocateIcons(itemCount);

			foreach ( EquipmentDef equipment in equipments )
			{
				ItemIcon icon = inventory.itemIcons[--itemCount];
				TooltipProvider tooltip = icon.tooltipProvider;

				icon.image.texture = equipment.pickupIconTexture;
				icon.stackText.enabled = false;

				tooltip.titleToken = equipment.nameToken;
				tooltip.bodyToken = equipment.pickupToken;
				tooltip.titleColor = ColorCatalog.GetColor(equipment.colorIndex);
			}
		}
	}
}
