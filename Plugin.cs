using BepInEx;
using HarmonyLib;
using HG;
using RoR2;
using RoR2.Stats;
using RoR2.UI;
using RoR2.UI.LogBook;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Console = RoR2.Console;
using UnlockBandit = RoR2.Achievements.CompleteThreeStagesAchievement;
using UnlockRejuvenationRack = RoR2.Achievements.CompleteThreeStagesWithoutHealingsAchievement.
		CompleteThreeStagesWithoutHealingServerAchievement;
using UnlockSentientMeatHook = RoR2.Achievements.LoopOnceAchievement;

[assembly: AssemblyVersion(Local.Fix.History.Plugin.versionNumber)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
		// Allow private member access via publicized assemblies.

namespace Local.Fix.History
{
	[BepInPlugin("local.fix.history", "HistoryFix", versionNumber)]
	public class Plugin : BaseUnityPlugin
	{
		public const string versionNumber = "0.4.1";
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

		[HarmonyPatch(typeof(Console), nameof(Console.LoadStartupConfigs))]
		[HarmonyPostfix]
		private static void PreserveHistory(Console __instance)
		{
			if ( int.MaxValue != MorgueManager.morgueHistoryLimit.value )
			{
				MorgueManager.morgueHistoryLimit.value = int.MaxValue;
				__instance.SaveArchiveConVars();
			}
		}

		[HarmonyPatch(typeof(StatManager), nameof(StatManager.OnServerGameOver))]
		[HarmonyPatch(typeof(UnlockBandit), nameof(UnlockBandit.Check))]
		[HarmonyPatch(typeof(UnlockRejuvenationRack), nameof(UnlockRejuvenationRack.Check))]
		[HarmonyPatch(typeof(UnlockSentientMeatHook), nameof(UnlockSentientMeatHook.Check))]
		[HarmonyTranspiler]
		private static IEnumerable<CodeInstruction> FixEclipseTracking(
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

			foreach ( EquipmentDef equipment in equipments )
			{
				if ( inventory.itemIcons.Any( item =>
						equipment.nameToken == item.tooltipProvider?.titleToken )
					) continue;

				inventory.AllocateIcons(inventory.itemIcons.Count + 1);

				ItemIcon icon = inventory.itemIcons.Last();
				TooltipProvider tooltip = icon.tooltipProvider;

				icon.image.texture = equipment.pickupIconTexture;
				icon.stackText.enabled = false;

				tooltip.titleToken = equipment.nameToken;
				tooltip.bodyToken = equipment.pickupToken;
				tooltip.titleColor = ColorCatalog.GetColor(equipment.colorIndex);

				ItemIcon example = inventory.itemIcons.First();
				if ( example.tooltipProvider?.bodyToken ==
						ItemCatalog.GetItemDef(example.itemIndex)?.descriptionToken )
					tooltip.bodyToken = equipment.descriptionToken;
			}
		}

		private static readonly Dictionary<Button, RunReport> entries =
				new Dictionary<Button, RunReport>();
		private static readonly List<Guid> deletedReports = new List<Guid>();

		[HarmonyPatch(typeof(LogBookController), nameof(LogBookController.BuildEntriesPage))]
		[HarmonyPrefix]
		private static void CleanUp() => entries.Clear();

		[HarmonyPatch(typeof(CategoryDef), nameof(CategoryDef.InitializeMorgue))]
		[HarmonyPostfix]
		private static void LoadReport(GameObject gameObject, Entry entry)
		{
			Button button = gameObject.GetComponent<Button>();
			if ( button != null && entry?.extraData is RunReport report )
			{
				entries[button] = report;

				if ( deletedReports.Contains(report.runGuid) )
					RemoveButton(button);
			}
		}

		[HarmonyPatch(typeof(MPButton), nameof(MPButton.OnPointerClick))]
		[HarmonyPostfix]
		private static void OnClick(Button __instance, PointerEventData eventData)
		{
			if ( eventData.button != PointerEventData.InputButton.Right ||
					! entries.TryGetValue(__instance, out RunReport report)
				) return;

			SimpleDialogBox dialog = SimpleDialogBox.Create();

			dialog.headerToken = new SimpleDialogBox.TokenParamsPair("Delete History Entry");
			dialog.descriptionToken = new SimpleDialogBox.TokenParamsPair(
					"Are you sure you want to permanently delete the selected run report?"
				);

			dialog.AddActionButton(deleteEntry, "DIALOG_OPTION_YES");
			dialog.AddCancelButton("CANCEL");

			void deleteEntry()
			{
				Guid identifier = report.runGuid;
				MorgueManager.storage.DeleteFile(
						MorgueManager.historyDirectory / identifier.ToString() + ".xml"
					);

				deletedReports.Add(identifier);
				RemoveButton(__instance);
			}
		}

		private static void RemoveButton(Button button)
		{
			button.enabled = false;
			button.GetComponentsInChildren<Image>().Do(
					image => image.enabled = false );

			Color filter = Color.grey.AlphaMultiplied(0.5f);

			button.GetComponentsInChildren<HGTextMeshProUGUI>().Do(
					text => text.color *= filter );
			button.GetComponentsInChildren<RawImage>().Do(
					icon => icon.color *= filter );
		}

		[HarmonyPatch(typeof(HGButton), nameof(HGButton.OnPointerEnter))]
		[HarmonyPostfix]
		private static void UpdateTooltip(HGButton __instance)
		{
			LanguageTextMeshController tooltip = __instance.hoverLanguageTextMeshController;
			if ( tooltip == null ||
					! entries.TryGetValue(__instance, out RunReport report)
				) return;

			RuleBook ruleBook = report.ruleBook;
			RunReport.PlayerInfo player = report.FindFirstPlayerInfo();
			StatSheet statistics = player.statSheet;
			DifficultyDef difficulty = DifficultyCatalog.GetDifficultyDef(
					ruleBook.FindDifficulty()
				);

			tooltip.token = RemoveSymbols(
					tooltip.token, Language.GetString("VOIDSURVIVOR_BODY_NAME")
				) + "\n<style=cSub>" +
					Language.GetString("LOBBY_CONTROL_PANEL_NAME") +
						$": " + Language.GetString( report.playerInfoCount > 1 ?
							"TITLE_MULTIPLAYER" : "TITLE_SINGLEPLAYER" ) + "\n" +
					Language.GetString("RULE_HEADER_DIFFICULTY") +
						$": { Language.GetString(difficulty.nameToken) }\n" +
					Language.GetString("RULE_HEADER_ARTIFACTS") +
						$": { GetArtifact(ruleBook) }\n" +
					Language.GetString("STATNAME_TOTALTIMEALIVE") +
						$": { statistics.GetStatDisplayValue(StatDef.totalTimeAlive) }\n" +
					Language.GetString("STATNAME_TOTALITEMSCOLLECTED").Split().First() +
						$": { player.itemStacks.Sum() }\n" +
					Language.GetString("STATNAME_TOTALSTAGESCOMPLETED") +
						$": { statistics.GetStatValueULong(StatDef.totalStagesCompleted) }\n" +
				"</style>" +
				"\n<style=cStack><i>" +
					"Right-click to delete..." +
				"</i></style>";
		}

		private static string RemoveSymbols(string source, string substring)
		{
			char[] replacement = substring.Where(
					character => char.IsLetter(character) || char.IsSeparator(character)
				).ToArray();

			return source.Replace(substring, new string(replacement));
		}

		private static string GetArtifact(RuleBook ruleBook)
		{
			string artifact = null;

			foreach ( RuleChoiceDef choice in ruleBook.choices )
			{
				if ( choice.artifactIndex == ArtifactIndex.None ||
						 choice.localIndex != ruleBook.GetRuleChoiceIndex(choice.ruleDef)
					) continue;

				if ( artifact is null )
				{
					artifact = ArtifactCatalog.GetArtifactDef(choice.artifactIndex)?.nameToken;
					if ( artifact is object )
						artifact = Language.GetString(artifact).Split().Last();
				}
				else
				{
					artifact = choice.localName;	// Language.GetString("OPTION_ON");
					break;
				}
			}

			return artifact ?? Language.GetString("OPTION_OFF");
		}
	}
}
