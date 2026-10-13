using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HG;
using RoR2;
using RoR2.Stats;
using RoR2.UI;
using RoR2.UI.LogBook;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Zio;
using Console = RoR2.Console;
using UnlockBandit = RoR2.Achievements.CompleteThreeStagesAchievement;
using UnlockRejuvenationRack = RoR2.Achievements.CompleteThreeStagesWithoutHealingsAchievement.
		CompleteThreeStagesWithoutHealingServerAchievement;
using UnlockSentientMeatHook = RoR2.Achievements.LoopOnceAchievement;

[assembly: AssemblyVersion(Local.Fix.History.Plugin.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace Local.Fix.History
{
	[BepInPlugin(identifier, "HistoryFix", version)]
	public class Plugin : BaseUnityPlugin
	{
		public const string identifier = "local.fix.history", version = "1.2.0";
		static ConfigEntry<uint> historyLimit; static ConfigEntry<bool> backupProfile;

		protected void Awake()
		{
			historyLimit = Config.Bind(
					section: "General",
					key: "History Limit",
					defaultValue: 60u,
					description:
						"Maximum number of run reports. Set to zero for unlimited entries."
				);

			backupProfile = Config.Bind(
					section: "General",
					key: "Profile Backup",
					defaultValue: false,
					description:
						"Enable monthly backup of player information on Steam platform."
				);

			Harmony.CreateAndPatchAll(typeof(Plugin));
		}

		[HarmonyPatch(typeof(MorgueManager), nameof(MorgueManager.EnforceHistoryLimit))]
		[HarmonyPrefix]
		static bool FixHistoryLimit()
		{
			if ( historyLimit.Value > 0 )
			{
				var historyFiles = CollectionPool<MorgueManager.HistoryFileInfo,
						List<MorgueManager.HistoryFileInfo>>.RentCollection();

				MorgueManager.GetHistoryFiles(historyFiles);
				historyFiles.Sort(( a, b ) => b.lastModified.CompareTo(a.lastModified));

				for ( int count = historyFiles.Count; count >= historyLimit.Value; )
					historyFiles[--count].Delete();

				CollectionPool<MorgueManager.HistoryFileInfo,
						List<MorgueManager.HistoryFileInfo>>.ReturnCollection(historyFiles);
			}
			return false;
		}

		[HarmonyPatch(typeof(Console), nameof(Console.SaveArchiveConVars))]
		[HarmonyPrefix]
		static void PreserveHistory()
		{
			int limit = int.MaxValue;

			switch ( historyLimit.Value )
			{
				case 0:
				case > int.MaxValue:
					break;

				default:
					limit = (int) historyLimit.Value - 1;
					break;
			}

			MorgueManager.morgueHistoryLimit.value = limit;
		}

		[HarmonyPatch(typeof(StatManager), nameof(StatManager.OnServerGameOver))]
		[HarmonyPatch(typeof(UnlockBandit), nameof(UnlockBandit.Check))]
		[HarmonyPatch(typeof(UnlockRejuvenationRack), nameof(UnlockRejuvenationRack.Check))]
		[HarmonyPatch(typeof(UnlockSentientMeatHook), nameof(UnlockSentientMeatHook.Check))]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> FixTypeForEclipse(IEnumerable<CodeInstruction> IL)
		{
			MethodInfo method = typeof(object).GetMethod(nameof(object.GetType));
			foreach ( CodeInstruction instruction in IL )
			{
				yield return instruction;

				if ( instruction.Calls(method) )
				{
					yield return Transpilers.EmitDelegate<Func<Type, Type>>(
							type => type == typeof(EclipseRun) ? typeof(Run) : type
						);
				}
			}
		}

		[HarmonyPatch(typeof(ItemIcon), nameof(ItemIcon.Awake))]
		[HarmonyTranspiler]
		static IEnumerable<CodeInstruction> FixForUserProfile(IEnumerable<CodeInstruction> IL,
				ILGenerator generator)
		{
			Label label = generator.DefineLabel();

			MethodInfo method = typeof(HUD).GetProperty(nameof(HUD.localUserViewer)).GetMethod;
			FieldInfo field = typeof(ItemIcon).GetField(
					nameof(ItemIcon.userProfile), AccessTools.all);

			foreach ( CodeInstruction instruction in IL )
			{
				if ( instruction.Calls(method) )
				{
					yield return new(OpCodes.Dup);
					yield return new(OpCodes.Brfalse, label);
				}
				else if ( instruction.StoresField(field) )
				{
					instruction.labels.Add(label);
				}

				yield return instruction;
			}
		}

		[HarmonyPatch(typeof(SaveSystemSteam), nameof(SaveSystemSteam.WriteToDisk))]
		[HarmonyPostfix]
		static void SaveProfile(FileOutput fileOutput)
		{
			UPath path = fileOutput.fileReference.path;
			if ( ! backupProfile.Value )
				return;

			string date = DateTime.Now.ToString("yyyy-MM");
			path = path.GetDirectory() / "History" / date / path.GetName();

			IFileSystem system = fileOutput.fileReference.fileSystem;
			byte[] data = fileOutput.contents;

			if ( system.FileExists(path) )
				return;

			using Stream stream = system.OpenFile(
					path, FileMode.Create, FileAccess.Write, FileShare.None);
			stream.Write(data, 0, data.Length);
		}

		class InventoryEquipment : MonoBehaviour {
				internal readonly List<EquipmentDef> equipments = new(); }

		[HarmonyPatch(typeof(GameEndReportPanelController),
				nameof(GameEndReportPanelController.SetPlayerInfo))]
		[HarmonyPrefix]
		static void GetEquipment(GameEndReportPanelController __instance,
				RunReport.PlayerInfo playerInfo)
		{
			ItemInventoryDisplay inventory = __instance.itemInventoryDisplay;
			if ( ! inventory ) return;

			var component = inventory.gameObject.GetComponent<InventoryEquipment>();

			if ( component ) component.equipments.Clear();
			else component = inventory.gameObject.AddComponent<InventoryEquipment>();

			foreach ( EquipmentIndex index in playerInfo.equipment )
			{
				EquipmentDef equipment = EquipmentCatalog.GetEquipmentDef(index);
				if ( equipment != null )
					component.equipments.Add(equipment);
			}
		}

		[HarmonyPatch(typeof(ItemInventoryDisplay), nameof(ItemInventoryDisplay.UpdateDisplay))]
		[HarmonyPostfix]
		static void AddEquipment(ItemInventoryDisplay __instance)
		{
			ItemInventoryDisplay inventory = __instance;
			if ( ! inventory || ! inventory.TryGetComponent(out InventoryEquipment component) )
				return;

			inventory.CalculateLayoutValues(out ItemInventoryDisplay.LayoutValues layout,
					inventory.itemIcons.Count + component.equipments.Count);

			foreach ( EquipmentDef equipment in component.equipments )
			{
				if ( inventory.itemIcons.Any(
						 item => equipment.pickupIconTexture == item.image?.texture )
					) continue;

				ItemIcon icon = Instantiate(
						inventory.itemIconPrefab, inventory.transform).GetComponent<ItemIcon>();

				inventory.itemIcons.Add(icon);
				inventory.LayoutIndividualIcon(layout, __instance.itemIcons.Count - 1);

				icon.image.texture = equipment.pickupIconTexture;
				icon.stackText.enabled = false;

				update(dlc3);
				update(dlc4);

				TooltipProvider tooltip = icon.tooltipProvider;

				tooltip.titleToken = equipment.nameToken;
				tooltip.bodyToken = equipment.pickupToken;
				tooltip.titleColor = ColorCatalog.GetColor(equipment.colorIndex);

				ItemIcon example = inventory.itemIcons.First();
				if ( example.tooltipProvider?.bodyToken ==
						ItemCatalog.GetItemDef(example.itemIndex)?.descriptionToken )
					tooltip.bodyToken = equipment.descriptionToken;

				[MethodImpl(MethodImplOptions.NoInlining)]
				void dlc3()
				{
					icon.durationImage.enabled = false;
				}

				[MethodImpl(MethodImplOptions.NoInlining)]
				void dlc4()
				{
					icon.asyncRawImage.SetAssetOrDirectReference(
							equipment.pickupIconSpriteReference, equipment.pickupIconSprite);
					icon.rentalImage.enabled = false;
					icon.disabledImage.enabled = false;
				}
			}

			inventory.OnIconCountChanged();

			static void update(Action patch)
			{
				try
				{
					patch();
				}
				catch ( Exception error )
				when ( error is MissingFieldException or TypeLoadException )
				{
					Debug.LogException(error);
				}
			}
		}

		class Report : MonoBehaviour { internal RunReport entry; }
		static readonly List<Guid> deleted = new();

		[HarmonyPatch(typeof(CategoryDef), nameof(CategoryDef.InitializeMorgue))]
		[HarmonyPostfix]
		static void LoadReport(GameObject gameObject, Entry entry)
		{
			Button button = gameObject.GetComponent<Button>();

			if ( button != null && entry?.extraData is RunReport report )
			{
				button.gameObject.AddComponent<Report>().entry = report;

				if ( deleted.Contains(report.runGuid) )
					RemoveButton(button);
			}
		}

		[HarmonyPatch(typeof(MPButton), nameof(MPButton.OnPointerClick))]
		[HarmonyPostfix]
		static void OnClick(Button __instance, PointerEventData eventData)
		{
			if ( eventData.button != PointerEventData.InputButton.Right ||
					! __instance.TryGetComponent(out Report component)
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
				Guid identifier = component.entry.runGuid;
				MorgueManager.storage.DeleteFile(
						MorgueManager.historyDirectory / identifier.ToString() + ".xml");

				deleted.Add(identifier);
				RemoveButton(__instance);
			}
		}

		static void RemoveButton(Button button)
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
		static void UpdateTooltip(HGButton __instance)
		{
			LanguageTextMeshController tooltip = __instance.hoverLanguageTextMeshController;
			if ( tooltip == null ||
					! __instance.TryGetComponent(out Report component)
				) return;

			RunReport report = component.entry;
			RunReport.PlayerInfo player = report.FindFirstPlayerInfo();
			StatSheet statistics = player.statSheet;
			DifficultyDef difficulty = DifficultyCatalog.GetDifficultyDef(
					report.ruleBook.FindDifficulty());

			tooltip.token = RemoveSymbols(
					tooltip.token, Language.GetString("VOIDSURVIVOR_BODY_NAME")
				) + "\n<style=cSub>" +
					Language.GetString("LOBBY_CONTROL_PANEL_NAME") +
						$": " + Language.GetString( report.playerInfoCount > 1 ?
							"TITLE_MULTIPLAYER" : "TITLE_SINGLEPLAYER" ) + "\n" +
					Language.GetString("RULE_HEADER_DIFFICULTY") +
						$": { Language.GetString(difficulty.nameToken) }\n" +
					Language.GetString("RULE_HEADER_ARTIFACTS") +
						$": { GetArtifact(report.ruleBook) }\n" +
					Language.GetString("STATNAME_TOTALTIMEALIVE") +
						$": { statistics.GetStatDisplayValue(StatDef.totalTimeAlive) }\n" +
					Language.GetString("STATNAME_TOTALITEMSCOLLECTED").Split().First() +
						$": { GetItemCount(player) }\n" +
					Language.GetString("STATNAME_TOTALSTAGESCOMPLETED") +
						$": { statistics.GetStatValueULong(StatDef.totalStagesCompleted) }\n" +
				"</style>" +
				"\n<style=cStack><i>" +
					"Right-click to delete..." +
				"</i></style>";
		}

		static string RemoveSymbols(string source, string substring)
		{
			char[] replacement = substring.Where(
					character => char.IsLetter(character) || char.IsSeparator(character)
				).ToArray();

			return source.Replace(substring, new string(replacement));
		}

		static string GetArtifact(RuleBook ruleBook)
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
					if ( artifact is not null )
						artifact = Language.GetString(artifact).Trim().Split().Last();
				}
				else
				{
					artifact = choice.localName;	// Language.GetString("OPTION_ON");
					break;
				}
			}

			return artifact ?? Language.GetString("OPTION_OFF");
		}

		static int GetItemCount(RunReport.PlayerInfo player)
		{
			int count = 0;
			for ( int index = 0; index < player.itemStacks.Length; ++index )
			{
				ItemDef item = ItemCatalog.GetItemDef((ItemIndex) index);
				if ( item && ! item.hidden && item.tier != ItemTier.NoTier )
					count += player.itemStacks[index];
			}

			return count;
		}
	}
}
