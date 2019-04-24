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
		private Collider airlock;
		private Part airlockPart;

		// hijacking the CrewHatchDialog to display alternative crew list
		private bool modclick = false;
		private bool hijack = false;
		private CrewHatchDialog chd;
		// HACK: check if stock KSP is done populating CrewHatchDialog before hijacking it
		// It takes stock KSP an inconsistent amount of time to activate CrewHatchController, and then spawn and populate the CrewHatchDialog.
		// We need to wait for stock KSP to finish populating the CrewHatchDialog, otherwise it will overwrite our stuff (instead of the other way round).
		// Lacking any formal indication that CrewHatchDialog is "done populating", we resort to testing if the dialog header has changed from its placeholder value.
		private int frame = 0;
		private static int framewait = 5;
		private const string CHD_NOTREADY_HEADER = "Part Title Crew";

		// BoardingPass ScreenMessages setting
		internal static bool boardingScreenMessages = true;

		// CLS support
		internal static bool useCLS = true;
		internal static ICLSAddon CLS = null;

		// CTI support
		internal static bool useCTI = true;
		#endregion

		#region Input / UI
		// emulate CrewHatchController, which uses LateUpdate(), not Update()
		private void LateUpdate() {
			if (FlightDriver.Pause)
				return;

			if (hijack)
				considerHijack();

			if (Mouse.CheckButtons(Mouse.GetAllMouseButtonsDown(),Mouse.Buttons.Left) && !MapView.MapIsEnabled) {
				modclick = modkey.GetKey();
				if ( (modclick || useCTI) && Physics.Raycast(FlightCamera.fetch.mainCamera.ScreenPointToRay(Input.mousePosition), out hit, RAYCAST_DIST, 1<<LAYER_PARTTRIGGER, QueryTriggerInteraction.Collide) ) {
					if (hit.collider.CompareTag(TAG_AIRLOCK)) {
						if (InputLockManager.IsAllLocked(ControlTypes.KEYBOARDINPUT)) {
							Log($"INFO: {(modclick?"mod+":"")}click detected on airlock, but input lock is active.");
							Debug.Log(InputLockManager.PrintLockStack());
						} else {
							Log($"INFO: {(modclick?"mod+":"")}click detected on airlock, standing by to hijack CrewHatchDialog.");
							airlock = hit.collider;
							hijack = true;
							chd = null;
							frame = 0;
						}
					}
				}
			}
		}

		private void considerHijack() {
			frame++;
			Log("INFO: considering hijack @ frame +" + frame);

			// can't do anything if CrewHatchController isn't active yet
			if (!CrewHatchController.fetch.Active) {
				// abort if CrewHatchController still isn't active after framewait -- e.g. player may have cancelled by clicking elsewhere
				if (frame >= framewait) {
					Log("INFO: CrewHatchController is still inactive, aborting hijack.");
					hijack = false;
					chd = null;
					frame = 0;
				}
				return;
			}

			// fetch CrewHatchDialog
			if (chd == null) {
				chd = CrewHatchController.fetch.CrewDialog;
				if (chd == null) {
					Log("ERROR: failed to obtain CrewHatchDialog.");
					return;
				}
			}

			// test if TextHeader has been changed from its placeholder value, as proxy indicator for CrewHatchDialog "done populating"
			if (CHD_NOTREADY_HEADER.Equals(chd.transform.GetChild(0).GetComponent<TextMeshProUGUI>().text))
				return;

			// stock KSP done setting up CrewHatchDialog contents, proceed with hijacking
			airlockPart = airlock.GetComponentInParent<Part>();
			if (modclick) doHijack();
			else doAugment();
			hijack = false;
			frame = 0;
		}

		private void doAugment() {
			Log($"INFO: augmenting CrewHatchDialog for airlock {airlock.gameObject.name} on part {airlockPart.partInfo.name} of {airlockPart.vessel.vesselName}");

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

			chd = null;
		}

		private void doHijack() {
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
			addCrewToList(listContainer, airlockPart);

			if (useCLS && !CLS.AllowUnrestrictedTransfers) {
				// Crew in other parts
				ICLSSpace clsSpace = CLS.getCLSVessel(airlockPart.vessel).Parts.Find(x => x.Part == airlockPart).Space;
				foreach (ICLSPart p in clsSpace.Parts) {
					if (p.Part != airlockPart)
						addCrewToList(listContainer.transform, p.Part);
				}
				// TextModuleCrew
				chd.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = Localizer.Format("#autoLOC_AirlockPlusAP001", clsSpace.Crew.Count, clsSpace.MaxCrew);
			}
			else {
				// Crew in other parts
				foreach (Part p in airlockPart.vessel.parts) {
					if (p != airlockPart)
						addCrewToList(listContainer.transform, p);
				}
				// TextModuleCrew
				chd.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = Localizer.Format("#autoLOC_AirlockPlusAP001", airlockPart.vessel.GetCrewCount(), airlockPart.vessel.GetCrewCapacity());
			}

			chd = null;
		}

		private void addCrewToList(Transform listContainer, Part p) {
			if (p.CrewCapacity == 0 || p.protoModuleCrew.Count == 0 || p.Modules.Contains<KerbalEVA>())
				return;

			foreach (ProtoCrewMember pcm in p.protoModuleCrew)
				if (!(pcm.inactive || pcm.outDueToG)) {
					List<DialogGUIBase> items = new List<DialogGUIBase>();
					if (useCTI) items.Add(CTIWrapper.CTI.getTrait(pcm.experienceTrait.Config.Name).makeDialogGUIImage(new Vector2(20,20),new Vector2()));
					items.Add(new DialogGUILabel($"<size=15><b>{pcm.name}</b></size>" + ((!useCTI && pcm.type == ProtoCrewMember.KerbalType.Tourist)?$"<size=10>{Localizer.Format("#autoLOC_AirlockPlusAP002")}</size>":""),true,false));
					items.Add(new DialogGUIButton($"<size=14>{Localizer.Format("#autoLOC_AirlockPlusAP003")}</size>",delegate{onBtnEVA(pcm);},48,24,true,options:null));
					DialogGUIHorizontalLayout h = new DialogGUIHorizontalLayout(false,false,0f,new RectOffset(4,0,0,0),TextAnchor.MiddleLeft,items.ToArray());
					Stack<Transform> layouts = new Stack<Transform>();
					layouts.Push(listContainer);
					GameObject go = h.Create(ref layouts, HighLogic.UISkin);
					go.transform.SetParent(listContainer);
				}
		}

		private void onBtnEVA(ProtoCrewMember pcm) {
			// HACK: Properly close the CrewHatchDialog from CrewHatchController's perspective.
			// Normally, the EVA/Transfer buttons call CrewHatchController.OnEVABtn()/OnTransferBtn(), but we need custom handling for kerbals from other parts.
			// CrewHatchDialog.Terminate() is public, but simply calling it might leave CrewHatchController dangling in some invalid state.
			// So we use reflection to invoke protected DismissDialog() which presumably is common to both CrewHatchController.OnEVABtn()/OnTransferBtn().
			typeof(CrewHatchController).GetMethod("DismissDialog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(CrewHatchController.fetch, null);

			Part kerbalPart = pcm.KerbalRef.InPart;
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

			// HACK: ensure HatchIsObstructed functions correctly
			// spawnEVA assumes fromPart corresponds to fromAirlock; these are passed on to FlightEVA.HatchIsObstructed
			// Using a different fromPart than what stock expects can lead to spurious results when checking hatches for obstruction
			// Fortunately, seems it can be fooled as long as we set the part's position to what it expects...
			Vector3 original = kerbalPart.transform.position;
			kerbalPart.transform.position = airlockPart.transform.position;
			Log("DEBUG: Attempting to spawn EVA...");
			if ( FlightEVA.fetch.spawnEVA(pcm,kerbalPart,airlock.transform) != null )
				Log("DEBUG: EVA spawned.");
			else
				Log("DEBUG: spawnEVA failed.");
			kerbalPart.transform.position = original;

			airlock = null;
			airlockPart = null;
		}
		#endregion

		#region MonoBehaviour life cycle
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
		#endregion

		private void Log(string s) {
			Debug.Log("[AirlockPlus] " + s);
		}
	}
}
