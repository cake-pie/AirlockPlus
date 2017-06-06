// © 2017 cake>pie
// All rights reserved

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using KSP.UI.Screens.Flight.Dialogs;
using KSP.Localization;
using TMPro;
using ConnectedLivingSpace;

namespace AirlockPlus
{
	// Provides enhanced vessel alighting features on top of stock CrewHatchController et al
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public sealed class AirlockPlus : MonoBehaviour
	{
		#region Variables
		// tag/layer constants
		internal static readonly int LAYER_PARTTRIGGER = 21;
		internal static readonly string TAG_AIRLOCK = "Airlock";
		//internal static readonly string TAG_LADDER = "Ladder";

		// screen messages
		private static ScreenMessage scrmsg_noEVA_upgd = new ScreenMessage( Localizer.Format("#autoLOC_294633") , 5f, ScreenMessageStyle.UPPER_CENTER);  //"Cannot disembark while off of Kerbin's surface.\nAstronaut Complex upgrade required."
		private static ScreenMessage scrmsg_noEVA_tour = new ScreenMessage( Localizer.Format("#autoLOC_294604") , 5f, ScreenMessageStyle.UPPER_CENTER);  //"Tourists may not disembark from the vessel."

		// key for activating AirlockPlus in conjunction with clicking on airlock
		private static KeyBinding modkey = null;

		// raycast vars
		private static float RAYCAST_DIST = 100f;
		private RaycastHit hit;

		// selected airlock part
		Part airlockPart;

		// hijacking the CrewHatchDialog to display alternative crew list
		private bool hijack = false;
		// HACK: delayed hijack
		// It usually takes stock KSP one frame to spawn the CrewHatchDialog, then one frame to actually populate it. But this delay is inconsistent.
		// On occasion it can take one or two more frames, no idea why. So just to be safe, we wait for five frames before hijacking the dialog.
		// Otherwise, when stock KSP populates the CrewHatchDialog, it will overwrite our stuff (instead of the other way round).
		//   Notes:
		//   This does not seem to be due our raycast occurring in LateUpdate.
		//   Moving our raycast to FixedUpdate does not appear to help improve the consistency of the delay, only makes our code more complex.
		//   Also, CrewHatchController maintains RaycastHit rayHit and has only LateUpdate, not FixedUpdate... so it is probably raycasting in LateUpdate too.
		private static int FRAMEWAIT = 5;
		private int frame = 0;

		// CLS support
		internal static bool useCLS = false;
		#endregion

		#region Input / UI
		// emulate CrewHatchController, which uses LateUpdate(), not Update()
		private void LateUpdate() {
			if (hijack) {
				frame++;
				// waiting a few frames for stock KSP to finish setting up the CrewHatchDialog so it won't overwrite our changes
				if (frame >= FRAMEWAIT) {
					frame = 0;
					hijack = false;
					doHijack();
				}
			}

			// Note: ControlTypes.KEYBOARDINPUT is locked when vessel lacks control.
			// So we cannot gate this alt+click behind InputLockManager.IsUnlocked(ControlTypes.KEYBOARDINPUT)
			if (modkey.GetKey() && Mouse.CheckButtons(Mouse.GetAllMouseButtonsDown(),Mouse.Buttons.Left)) {
				if ( Physics.Raycast(FlightCamera.fetch.mainCamera.ScreenPointToRay(Input.mousePosition), out hit, RAYCAST_DIST, 1<<LAYER_PARTTRIGGER, QueryTriggerInteraction.Collide) ) {
					if (hit.collider.CompareTag(TAG_AIRLOCK)) {
						hijack = true;
						frame = 0; // reset frame wait count in case of clicks in rapid succession
					}
				}
			}
		}

		private void doHijack() {
			// abort if CrewHatchController isn't active
			if (!CrewHatchController.fetch.Active)
				return;

			airlockPart = hit.collider.GetComponentInParent<Part>();

			Debug.Log("[AirlockPlus] INFO: hijacking CrewHatchDialog for airlock " + hit.collider.gameObject.name + " on part " + airlockPart.partInfo.name + " of " + airlockPart.vessel.vesselName);
			CrewHatchDialog chd = CrewHatchController.fetch.CrewDialog;
			if (chd == null) {
				Debug.Log("[AirlockPlus] ERROR: failed to obtain CrewHatchDialog.");
				return;
			}

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
				return;
			}
			listContainer.GetChild(0).gameObject.SetActive(false);

			// Crew in airlock part
			addCrewToList(listContainer, airlockPart);
				// Crew in other parts
			if (useCLS) {
				ICLSSpace clsSpace = CLSClient.GetCLS().getCLSVessel(airlockPart.vessel).Parts.Find(x => x.Part == airlockPart).Space;
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
		}

		private void addCrewToList(Transform listContainer, Part p) {
			if (p.CrewCapacity == 0 || p.protoModuleCrew.Count == 0)
				return;

			foreach (ProtoCrewMember pcm in p.protoModuleCrew) {
				bool isTourist = (pcm.type == ProtoCrewMember.KerbalType.Tourist);

				DialogGUIBase[] items = new DialogGUIBase[2];
				items[0] = new DialogGUILabel("<size=15><b>"+pcm.name+"</b></size>" + (isTourist?"<size=10> "+ Localizer.Format("#autoLOC_AirlockPlusAP002") +"</size>":"") ,true,true);
				items[1] = new DialogGUIButton("<size=14>"+ Localizer.Format("#autoLOC_AirlockPlusAP003") +"</size>",delegate{onBtnEVA(pcm);},48,24,true,null);
				// TODO: grayed out button: this isn't going to work outside of a PopupDialog =(
				//if (isTourist) items[1].OptionEnabledCondition = ()=>false;
				DialogGUIHorizontalLayout h = new DialogGUIHorizontalLayout(false,false,0f,new RectOffset(4,0,0,0),TextAnchor.MiddleLeft,items);
				Stack<Transform> layouts = new Stack<Transform>();
				layouts.Push(listContainer);
				GameObject go = h.Create(ref layouts, HighLogic.UISkin);
				go.transform.SetParent(listContainer);
			}
		}

		private void onBtnEVA(ProtoCrewMember pcm) {
			// HACK: we have to resort to this way to close the CrewHatchDialog while ensuring CrewHatchController remains in a coherent state
			// Normally, the EVA/Transfer buttons call OnEVABtn/OnTransferBtn in CrewHatchController, so it can react accordingly.
			// If we simply call CrewHatchDialog.Terminate() it would leave CrewHatchController dangling in an active state!
			CrewHatchController.fetch.DisableInterface();
			CrewHatchController.fetch.EnableInterface();

			// sanity checks, in case of unexpected death, destruction or separation
			if (pcm.rosterStatus != ProtoCrewMember.RosterStatus.Assigned || pcm.inactive || pcm.outDueToG) return;
			if (pcm.KerbalRef.InVessel != airlockPart.vessel) return;

			// prohibitions
			if (pcm.type == ProtoCrewMember.KerbalType.Tourist) {
				ScreenMessages.PostScreenMessage(scrmsg_noEVA_tour);
				return;
			}
			if (!GameVariables.Instance.EVAIsPossible( GameVariables.Instance.UnlockedEVA( ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex) ) , pcm.KerbalRef.InVessel)) {
				ScreenMessages.PostScreenMessage(scrmsg_noEVA_upgd);
				return;
			}

			FlightEVA.fetch.spawnEVA(pcm,pcm.KerbalRef.InPart,hit.collider.transform);
		}
		#endregion

		#region MonoBehaviour life cycle
		private void Start() {
			Debug.Log("[AirlockPlus] INFO: Starting AirlockPlus...");
			modkey = GameSettings.MODIFIER_KEY;
			Debug.Log("[AirlockPlus] INFO: MODIFIER_KEY key is " + modkey.primary.ToString());

			// CLS support
			useCLS = CLSClient.CLSInstalled;
			Debug.Log("[AirlockPlus] INFO: CLS support is " + (useCLS?"on":"off"));
		}
		#endregion
	}
}
