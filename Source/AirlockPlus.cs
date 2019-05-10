// © 2017-2019 cake>pie
// All rights reserved

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens.Flight.Dialogs;
using KSP.Localization;
using TMPro;
using ConnectedLivingSpace;

namespace AirlockPlus
{
	// Provides enhanced vessel alighting features on top of stock CrewHatchController & CrewHatchDialog
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public sealed class AirlockPlus : MonoBehaviour
	{
		#region Variables
		// singleton
		internal static AirlockPlus Instance;

		// tag/layer constants
		internal const int LAYER_PARTTRIGGER = 21;
		internal const string TAG_AIRLOCK = "Airlock";
		//internal const string TAG_LADDER = "Ladder";

		// screen messages
		private static ScreenMessage scrmsg_noEVA_upgd = new ScreenMessage( Localizer.Format("#autoLOC_294633") , 5f, ScreenMessageStyle.UPPER_CENTER);  //"Cannot disembark while off of Kerbin's surface.\nAstronaut Complex upgrade required."
		private static ScreenMessage scrmsg_noEVA_tour = new ScreenMessage( Localizer.Format("#autoLOC_294604") , 5f, ScreenMessageStyle.UPPER_CENTER);  //"Tourists may not disembark from the vessel."

		// key for activating AirlockPlus in conjunction with clicking on airlock
		private static KeyBinding modkey = null;

		// raycast vars
		private const float RAYCAST_DIST = 100f;
		private RaycastHit hit;

		// selected airlock and part
		internal Collider airlock;
		internal Part airlockPart;
		internal Part kerbalPart;

		// hijacking the CrewHatchDialog to display alternative crew list
		private bool hijack = false;
		private bool foundActiveCHC = false;
		private CrewHatchDialog chd;
		// HACK: we have to do our own input handling and try to match it up correctly with stock CrewHatchDialog activation
		// these provide timeout mechanism if a corresponding dialog activation does not materialize after user input
		private int frame = 0;
		private static int framewait = 5;

		// BoardingPass ScreenMessages setting
		internal static bool boardingScreenMessages = true;

		// CLS support
		internal static bool useCLS = true;
		internal static ICLSAddon CLS = null;

		// CTI support
		internal static bool useCTI = true;
		#endregion

		#region Input Handling
		private void LateUpdate() {
			if (FlightDriver.Pause)
				return;

			if (hijack)
				CheckHijackTimeout();

			if (
				modkey.GetKey() && Mouse.CheckButtons(Mouse.GetAllMouseButtonsDown(),Mouse.Buttons.Left) && !MapView.MapIsEnabled &&
				Physics.Raycast(FlightCamera.fetch.mainCamera.ScreenPointToRay(Input.mousePosition), out hit, RAYCAST_DIST, 1<<LAYER_PARTTRIGGER, QueryTriggerInteraction.Collide) &&
				hit.collider.CompareTag(TAG_AIRLOCK)
			) {
				if (InputLockManager.IsAllLocked(ControlTypes.KEYBOARDINPUT)) {
					Log("INFO: mod+click detected on airlock, but input lock is active.");
					Debug.Log(InputLockManager.PrintLockStack());
				} else {
					Log("INFO: mod+click detected on airlock, standing by to hijack CrewHatchDialog.");
					airlock = hit.collider;
					airlockPart = null;
					kerbalPart = null;
					hijack = true;
					foundActiveCHC = false;
					chd = null;
					frame = 0;
				}
			}
		}

		// HACK: matching our input handling with stock response to its own input handling
		// Presumably CrewHatchController handles player input and does raycasting to detect clicks on airlocks
		// but none of that is exposed to us via API so we are forced to do our own and then try to match it up
		// correctly with the stock CrewHatchDialog activation.
		// It seems to take stock KSP an inconsistent amount of time to instantiate and populate CrewHatchDialog
		// We need to wait for stock KSP to finish populating the CrewHatchDialog before augmenting or hijacking it
		// otherwise it will overwrite our stuff (instead of the other way round)
		// Based on tests using Harmony prefixes/postfixes to add debugging log outputs before and after
		// various methods in CrewHatchController and CrewHatchDialog we know the following behaviors
		// - CrewHatchController.SpawnCrewDialog() causes
		//   - Active false -> true
		//   - CrewDialog null -> not null, TextHeader = Part Title Crew
		// - CrewHatchDialog.CreatePanelContent() causes
		//   - TextHeader = Part Title Crew -> <part name> Crew
		//   - list populated with crew in the part
		// - CrewHatchController.DismissDialog() gets called by OnEVABtn(), OnTransferBtn() as well as others
		// - CrewHatchDialog.Terminate() gets called by CrewHatchController.DismissDialog() as well as others
		// We have to accommodate any difference in implementation between our input handling and that of stock,
		// which means CrewHatchController may not activate or CrewHatchDialog may not spawn, so implement a
		// timeout mechanism if corresponding dialog activation does not materialize after user input
		// We also have to handle situations where the dialog activation may be cancelled before it is ready
		// Logic is as follows
		// - any fresh detected mod+click input resets the timeout and discards old information
		//   assume we are now trying to match up with the new user input
		// - track when CrewHatchController becomes active after input detected
		//   if it becomes inactive again, treat as cancellation and abort
		// - when CrewHatchController is active, look for non-null member CrewDialog
		//   if it goes back to being null, treat as cancellation and abort
		// - if the detected CrewDialog runs Terminate before becoming ready, treat as cancellation and abort
		// - if any CrewHatchDialog becomes ready at any time
		//   - treat as hijack target if we are anticipating one
		//   - otherwise treat as augmentation target if CTI is available
		// - if number of frames passed has exceeded framewait and:
		//   - CrewHatchController is still inactive: abort
		//   - CrewHatchController active but CrewDialog still null: abort
		//   - CrewHatchController active, CrewDialog instantiated but not ready: continue waiting
		//     - CrewDialog becomes ready: proceed
		//     - CrewHatchController becomes active, or CrewDialog becomes null or terminated: abort
		private void CheckHijackTimeout() {
			frame++;
			Log("INFO: checking hijack timeout @ frame +" + frame);

			if (!foundActiveCHC) {
				if (CrewHatchController.fetch.Active) {
					Log("INFO: found active CrewHatchController.");
					foundActiveCHC = true;
				} else {
					AbortIfTimeout();
					return;
				}
			} else {
				if (!CrewHatchController.fetch.Active) {
					Log("INFO: CrewHatchController became inactive, aborting hijack.");
					AbortHijack();
					return;
				}
			}

			if (chd == null) {
				if (CrewHatchController.fetch.CrewDialog != null) {
					Log("INFO: CrewDialog instance located.");
					chd = CrewHatchController.fetch.CrewDialog;
				} else {
					AbortIfTimeout();
					return;
				}
			} else {
				if (CrewHatchController.fetch.CrewDialog == null) {
					Log("INFO: CrewDialog became null, aborting hijack.");
					AbortHijack();
					return;
				}
			}

			// CrewHatchController is active and CrewHatchDialog instance exists
			// awaiting OnCHDReady()
		}

		private void AbortIfTimeout() {
			if (frame >= framewait) {
				Log("INFO: still no CrewHatchDialog instance located, aborting hijack.");
				AbortHijack();
			}
		}

		private void AbortHijack() {
			hijack = false;
			foundActiveCHC = false;
			chd = null;
			frame = 0;
		}

		// stock KSP done setting up CrewHatchDialog contents, proceed with hijacking
		internal void OnCHDReady(CrewHatchDialog chdr) {
			if (chd == null)
				chd = chdr;
			// hold on to chd if hijacking
			// allows cleanup via OnCHDTerminated() regardless of whether any kerbal went EVA
			if (hijack) DoHijack();
			else {
				if (useCTI) DoAugment();
				chd = null;
			}
			hijack = false;
			foundActiveCHC = false;
			frame = 0;
		}

		internal void OnCHDTerminated(CrewHatchDialog chdt) {
			if (chd == chdt) {
				if (hijack) AbortHijack();
				chd = null;
				airlock = null;
				airlockPart = null;
				kerbalPart = null;
			}
		}
		#endregion

		#region UI
		private void DoAugment() {
			Log($"INFO: augmenting CrewHatchDialog for part {chd.Part.partInfo.name} of {chd.Part.vessel.vesselName}");

			// Content transform of Scroll View
			Transform listContainer = chd.GetComponentInChildren<ContentSizeFitter>().transform;
			// Add icons
			for (int i = listContainer.childCount-1; i > 0; i--) {
				GameObject icon = CTIWrapper.CTI.getTrait( listContainer.GetChild(i).GetComponent<CrewHatchDialogWidget>().protoCrewMember.experienceTrait.Config.Name ).makeGameObject();
				LayoutElement elem = icon.AddComponent<LayoutElement>();
				elem.minWidth = elem.minHeight = elem.preferredWidth = elem.preferredHeight = 20;
				icon.transform.SetParent(listContainer.GetChild(i).transform,false);
				icon.transform.SetAsFirstSibling();
				icon.SetActive(true);
			}
		}

		private void DoHijack() {
			airlockPart = airlock.GetComponentInParent<Part>();
			Log($"INFO: hijacking CrewHatchDialog for airlock {airlock.gameObject.name} on part {airlockPart.partInfo.name} of {airlockPart.vessel.vesselName}");

			// TextHeader
			chd.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text = Localizer.Format("#autoLOC_AirlockPlus00000");

			// Content transform of Scroll View
			Transform listContainer = chd.GetComponentInChildren<ContentSizeFitter>().transform;
			// Wipe the slate clean
			for (int i = listContainer.childCount-1; i > 0; i--)
				listContainer.GetChild(i).gameObject.SetActive(false);

			// EmptyModuleText
			if (airlockPart.vessel.GetCrewCount() == 0) {
				listContainer.GetChild(0).gameObject.SetActive(true);
				chd = null;
				return;
			}
			listContainer.GetChild(0).gameObject.SetActive(false);

			// Crew in airlock part
			AddCrewToList(listContainer, airlockPart);

			if (useCLS && !CLS.AllowUnrestrictedTransfers) {
				// Crew in other parts
				ICLSSpace clsSpace = CLS.getCLSVessel(airlockPart.vessel).Parts.Find(x => x.Part == airlockPart).Space;
				foreach (ICLSPart p in clsSpace.Parts) {
					if (p.Part != airlockPart)
						AddCrewToList(listContainer.transform, p.Part);
				}
				// TextModuleCrew
				chd.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = Localizer.Format("#autoLOC_AirlockPlusAP001", clsSpace.Crew.Count, clsSpace.MaxCrew);
			}
			else {
				// Crew in other parts
				foreach (Part p in airlockPart.vessel.parts) {
					if (p != airlockPart)
						AddCrewToList(listContainer.transform, p);
				}
				// TextModuleCrew
				chd.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = Localizer.Format("#autoLOC_AirlockPlusAP001", airlockPart.vessel.GetCrewCount(), airlockPart.vessel.GetCrewCapacity());
			}
		}

		private void AddCrewToList(Transform listContainer, Part p) {
			if (p.CrewCapacity == 0 || p.protoModuleCrew.Count == 0 || p.Modules.Contains<KerbalEVA>())
				return;

			foreach (ProtoCrewMember pcm in p.protoModuleCrew)
				if (!(pcm.inactive || pcm.outDueToG)) {
					List<DialogGUIBase> items = new List<DialogGUIBase>();
					if (useCTI) items.Add(CTIWrapper.CTI.getTrait(pcm.experienceTrait.Config.Name).makeDialogGUIImage(new Vector2(20,20),new Vector2()));
					items.Add(new DialogGUILabel($"<size=15><b>{pcm.name}</b></size>" + ((!useCTI && pcm.type == ProtoCrewMember.KerbalType.Tourist)?$"<size=10>{Localizer.Format("#autoLOC_AirlockPlusAP002")}</size>":""),true,false));
					items.Add(new DialogGUIButton($"<size=14>{Localizer.Format("#autoLOC_AirlockPlusAP003")}</size>",delegate{OnBtnEVA(pcm);},48,24,true,options:null));
					DialogGUIHorizontalLayout h = new DialogGUIHorizontalLayout(false,false,0f,new RectOffset(4,0,0,0),TextAnchor.MiddleLeft,items.ToArray());
					Stack<Transform> layouts = new Stack<Transform>();
					layouts.Push(listContainer);
					GameObject go = h.Create(ref layouts, HighLogic.UISkin);
					go.transform.SetParent(listContainer);
				}
		}

		private void OnBtnEVA(ProtoCrewMember pcm) {
			kerbalPart = pcm.KerbalRef.InPart;
			Log($"INFO: EVA button pressed; {pcm.name} in part {kerbalPart.partInfo.name} of {airlockPart.vessel.vesselName} attempting to exit via airlock {airlock.gameObject.name} on part {airlockPart.partInfo.name}");

			// sanity checks, in case of unexpected death, destruction or separation
			if (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Assigned || pcm.inactive || pcm.outDueToG) return;
			if (airlockPart.State == PartStates.DEAD || pcm.KerbalRef.InVessel != airlockPart.vessel) return;

			// prohibitions
			if (pcm.type == ProtoCrewMember.KerbalType.Tourist) {
				ScreenMessages.PostScreenMessage(scrmsg_noEVA_tour);
				return;
			}
			if (!GameVariables.Instance.EVAIsPossible( GameVariables.Instance.UnlockedEVA( ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex) ) , pcm.KerbalRef.InVessel)) {
				ScreenMessages.PostScreenMessage(scrmsg_noEVA_upgd);
				return;
			}

			// HACK: FlightEVA.spawnEVA expects fromPart to be the part containing the kerbal
			// AND the part having the airlock i.e. they need to be one and the same
			// We've circumvented this with patches for FlightEVA in HarmonyPatches.cs
			Log("DEBUG: Attempting to spawn EVA...");
			if ( FlightEVA.fetch.spawnEVA(pcm,kerbalPart,airlock.transform) != null )
				Log("DEBUG: EVA spawned.");
			else
				Log("DEBUG: spawnEVA failed.");

			// HACK: Properly close the CrewHatchDialog from CrewHatchController's perspective.
			// Normally, the EVA/Transfer buttons call CrewHatchController.OnEVABtn()/OnTransferBtn(), but we need custom handling for kerbals from other parts.
			// CrewHatchDialog.Terminate() is public, but simply calling it might leave CrewHatchController dangling in some invalid state.
			// So we use reflection to invoke protected DismissDialog() which presumably is common to both CrewHatchController.OnEVABtn()/OnTransferBtn().
			typeof(CrewHatchController).GetMethod("DismissDialog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(CrewHatchController.fetch, null);
			// OnCHDTerminated() will take care of cleanup
		}
		#endregion

		#region MonoBehaviour life cycle
		private void Awake() {
			if (Instance != null) {
				Destroy(gameObject);
				return;
			}
			if (!Harmony.Patcher.harmonyAvailable) {
				Log("ERROR: Harmony dependency not available, aborting.");
				Destroy(gameObject);
				return;
			}
			Instance = this;
		}

		private void Start() {
			Log("INFO: Starting AirlockPlus...");
			modkey = GameSettings.MODIFIER_KEY;
			Log("INFO: MODIFIER_KEY key is " + modkey.primary.ToString());

			ConfigNode node = GameDatabase.Instance.GetConfigNode("AirlockPlus/Settings/AirlockPlusSettings");
			if (node != null) {
				Log("INFO: reading settings file...\n" + node);
				node.TryGetValue("framewait", ref framewait);
				node.TryGetValue("boardingScreenMessages", ref boardingScreenMessages);
				node.TryGetValue("useCLS", ref useCLS);
			}

			// CLS support
			if (useCLS) useCLS = ( CLS = CLSClient.GetCLS() ) != null;
			Log("INFO: CLS support is " + (useCLS?"on":"off"));

			// CTI support
			useCTI = CTIWrapper.initCTIWrapper() && CTIWrapper.CTI.Loaded;
			Log("INFO: CTI support is " + (useCTI?"on":"off"));
		}

		private void OnDestroy() {
			if (Instance == this)
				Instance = null;
		}
		#endregion

		private void Log(string s) {
			Debug.Log("[AirlockPlus] " + s);
		}
	}
}
