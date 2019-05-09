// © 2017-2019 cake>pie
// All rights reserved

using System;
using System.Collections.Generic;
using UnityEngine;
using KSP.Localization;
using ConnectedLivingSpace;

namespace AirlockPlus
{
	// Provides enhanced vessel boarding functionality from EVA
	public sealed class BoardingPass : PartModule
	{
		#region Variables
		// screen messages
		private static ScreenMessage scrmsgKeys       = new ScreenMessage( Localizer.Format("#autoLOC_AirlockPlusBP001","B") , float.MaxValue, ScreenMessageStyle.LOWER_CENTER);
		private static ScreenMessage scrmsgVesFull    = new ScreenMessage( Localizer.Format("#autoLOC_AirlockPlusBP002") , 5f, ScreenMessageStyle.UPPER_CENTER);
		private static ScreenMessage scrmsgCLSFull    = new ScreenMessage( Localizer.Format("#autoLOC_AirlockPlusBP002c") , 5f, ScreenMessageStyle.UPPER_CENTER);
		private static ScreenMessage scrmsgPartSelect = new ScreenMessage( Localizer.Format("#autoLOC_AirlockPlusBP003","#00f9ea") , 15f, ScreenMessageStyle.UPPER_CENTER);
		private static ScreenMessage scrmsgPartFull   = new ScreenMessage( Localizer.Format("#autoLOC_111558") , 3f, ScreenMessageStyle.UPPER_CENTER);

		// highlighter colors
		private static readonly Color COLOR_AVAIL = Highlighting.Highlighter.colorPartTransferDestHighlight;
		private static readonly Color COLOR_AVAIL_OVR = Highlighting.Highlighter.colorPartTransferDestHover;
		private static readonly Color COLOR_FULL = Highlighting.Highlighter.colorPartTransferSourceHighlight;
		private static readonly Color COLOR_FULL_OVR = Highlighting.Highlighter.colorPartTransferSourceHover;

		// key for EVA boarding
		private static KeyBinding boardkey = null;

		// KerbalEVA associated with this kerbal
		private KerbalEVA keva = null;

		// disable functionality when in map view
		private bool inMap = false;

		// the part that the kerbal would board under stock rules
		private Part tgtAirlockPart = null;

		// auto boarding mode vars
		private bool autoBoardingFull = false;

		// manual boarding mode vars
		internal bool manualBoarding = false;
		private Part lastHovered = null;
		private Dictionary<uint,Part> highlightParts = new Dictionary<uint,Part>();

		// stock command hint removal
		private List<ScreenMessage> smToRemove = new List<ScreenMessage>();

		// CLS support
		private Action _BoardAuto;
		private Action _BoardManualListParts;
		#endregion

		#region Trigger Handling
		private void OnTriggerStay(Collider other) {
			// Do nothing unless this KerbalEVA is the active vessel in flight, in vessel view, and not already at an airlock
			if (tgtAirlockPart != null || !HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel || inMap)
				return;

			if (other.gameObject.layer == AirlockPlus.LAYER_PARTTRIGGER && other.CompareTag(AirlockPlus.TAG_AIRLOCK)) {
				tgtAirlockPart = other.GetComponentInParent<Part>();
				UpdateScreenMessages();
			}
		}

		private void OnTriggerExit(Collider other) {
			// Do nothing unless this KerbalEVA is the active vessel in flight, in vessel view.
			if (!HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel || inMap)
				return;

			if (other.gameObject.layer == AirlockPlus.LAYER_PARTTRIGGER && other.CompareTag(AirlockPlus.TAG_AIRLOCK) && tgtAirlockPart == other.GetComponentInParent<Part>()) {
				tgtAirlockPart = null;
				if (manualBoarding)
					BoardManualCxl();
				else
					UpdateScreenMessages();
			}
		}
		#endregion

		#region Input / UI
		public override void OnUpdate() {
			// Do nothing unless this KerbalEVA is the active vessel in flight, in vessel view, and positioned to enter an airlock
			if (tgtAirlockPart == null || !HighLogic.LoadedSceneIsFlight || !vessel.isActiveVessel || inMap)
				return;

			// HACK: re-enable KerbalEVA one frame after blocking stock boarding from registering alongside auto boarding
			if (autoBoardingFull) {
				keva.enabled = true;
				autoBoardingFull = false;
			}

			if (manualBoarding) {
				// Not checking for input locks in here, we should be holding the lock on all but camera controls at this juncture

				// Use GetKeyUp instead of GetKey/GetKeyDown prevents triggering game pause immediately upon releasing input lock
				if (Input.GetKeyUp(KeyCode.Escape)) {
					BoardManualCxl();
					return;
				}

				if (lastHovered != Mouse.HoveredPart) {
					if (lastHovered != null && highlightParts.ContainsKey(lastHovered.flightID))
						lastHovered.highlighter.ConstantOnImmediate( SpaceAvail(lastHovered) ? COLOR_AVAIL : COLOR_FULL );
					lastHovered = Mouse.HoveredPart;
					if (lastHovered != null && highlightParts.ContainsKey(lastHovered.flightID))
						lastHovered.highlighter.ConstantOnImmediate( SpaceAvail(lastHovered) ? COLOR_AVAIL_OVR : COLOR_FULL_OVR );
				}

				if (Mouse.CheckButtons(Mouse.GetAllMouseButtonsDown(),Mouse.Buttons.Left) && lastHovered != null)
					BoardManualSel();
			}
			else {
				if ( Input.GetKey(KeyCode.LeftShift) && boardkey.GetKeyUp() ) {
					if (InputLockManager.IsAllLocked(ControlTypes.KEYBOARDINPUT)) {
						Log("INFO: shift+board detected, but input lock is active.");
						Debug.Log(InputLockManager.PrintLockStack());
					} else {
						_BoardAuto();
					}
				}
				if ( Input.GetKey(KeyCode.LeftControl) && boardkey.GetKeyUp() ) {
					if (InputLockManager.IsAllLocked(ControlTypes.KEYBOARDINPUT)) {
						Log("INFO: ctrl+board detected, but input lock is active.");
						Debug.Log(InputLockManager.PrintLockStack());
					} else {
						BoardManual();
					}
				}
			}
		}

		private void UpdateScreenMessages() {
			if (tgtAirlockPart == null) {
				ScreenMessages.RemoveMessage(scrmsgKeys);
				ScreenMessages.RemoveMessage(scrmsgVesFull);
				ScreenMessages.RemoveMessage(scrmsgCLSFull);
				ScreenMessages.RemoveMessage(scrmsgPartSelect);
				ScreenMessages.RemoveMessage(scrmsgPartFull);
				return;
			}
			if (manualBoarding) {
				ScreenMessages.RemoveMessage(scrmsgKeys);
				ScreenMessages.RemoveMessage(scrmsgVesFull);
				ScreenMessages.RemoveMessage(scrmsgCLSFull);
				return;
			}
			ScreenMessages.RemoveMessage(scrmsgPartSelect);
			ScreenMessages.RemoveMessage(scrmsgPartFull);

			if (AirlockPlus.boardingScreenMessages)
				ScreenMessages.PostScreenMessage(scrmsgKeys);
		}
		#endregion

		#region Boarding Logic
		private void BoardAuto() {
			Log($"INFO: {vessel.vesselName} auto boarding {tgtAirlockPart.vessel.vesselName} via {tgtAirlockPart.partInfo.name}");

			// check in case of full vessel first
			if (tgtAirlockPart.vessel.GetCrewCount() >= tgtAirlockPart.vessel.GetCrewCapacity()) {
				Log("INFO: Auto boarding failed - vessel full");
				ScreenMessages.PostScreenMessage(scrmsgVesFull);

				// HACK: temporarily disable KerbalEVA for one update frame to prevent stock boarding from being registered alongside auto boarding
				// this prevents "spurious" stock "Cannot board a full module" message appearing alongside our auto boarding "Cannot board a full vessel"
				keva.enabled = false;
				autoBoardingFull = true;

				return;
			}

			// find part to board
			Part dest = null;
			if (SpaceAvail(tgtAirlockPart)) {
				// board the part itself if possible
				dest = tgtAirlockPart;
			} else {
				foreach (Part p in tgtAirlockPart.vessel.parts) {
					if (SpaceAvail(p)) {
						dest = p;
						break;
					}
				}
			}
			if (dest == null) {
				Log($"ERROR: Auto boarding target vessel at {tgtAirlockPart.vessel.GetCrewCount()}/{tgtAirlockPart.vessel.GetCrewCapacity()} of capacity, but somehow unable to find a part with space?!");
				return;
			}

			keva.BoardPart(dest);
		}

		private void BoardManual() {
			Log($"INFO: {vessel.vesselName} manual boarding mode initiated for {tgtAirlockPart.vessel.vesselName} via {tgtAirlockPart.partInfo.name}");

			// UI
			ScreenMessages.PostScreenMessage(scrmsgPartSelect);
			manualBoarding = true;
			UpdateScreenMessages();

			// find candidate parts to highlight
			_BoardManualListParts();

			// Input Locking. ControlTypes.All seems rather brute force but this seems to correspond closely to the stock crew transfer behavior.
			InputLockManager.SetControlLock(ControlTypes.All & ~ControlTypes.CAMERACONTROLS, "AirlockPlusBoardingPass");
			// ...except that we want to prevent stock behavior of clicking on airlocks, too
			CrewHatchController.fetch.DisableInterface();

			// register for onVesselStandardModification to be notified in case the vessel being boarded undergoes unexpected modification e.g. crashing / burning up in atmosphere / blowing up
			GameEvents.onVesselStandardModification.Add(onVesselStandardModification);
		}

		private void BoardManualListParts() {
			foreach (Part p in tgtAirlockPart.vessel.parts) {
				if (p.CrewCapacity>0) {
					highlightParts.Add(p.flightID,p);
					p.Highlight(false);
					p.SetHighlightType(Part.HighlightType.Disabled);
					p.highlighter.ConstantOn(SpaceAvail(p) ? COLOR_AVAIL : COLOR_FULL);
				}
			}
		}

		private void BoardManualSel() {
			if (!highlightParts.ContainsKey(lastHovered.flightID)) return;
			if (SpaceAvail(lastHovered)) {
				Log($"INFO: {vessel.vesselName} manually boarding {lastHovered.partInfo.name} of {tgtAirlockPart.vessel.vesselName} via {tgtAirlockPart.partInfo.name}");
				keva.BoardPart(lastHovered);
			}
			else
				ScreenMessages.PostScreenMessage(scrmsgPartFull);
		}

		private void BoardManualCxl() {
			Log("INFO: Manual boarding mode terminated.");
			manualBoarding = false;
			UpdateScreenMessages();

			foreach (Part p in highlightParts.Values) {
				p.highlighter.ConstantOffImmediate();
				p.SetHighlightDefault();
			}
			highlightParts.Clear();

			InputLockManager.RemoveControlLock("AirlockPlusBoardingPass");
			CrewHatchController.fetch.EnableInterface();

			GameEvents.onVesselStandardModification.Remove(onVesselStandardModification);
		}

		private void BoardCxl() {
			tgtAirlockPart = null;
			if (autoBoardingFull) {
				keva.enabled = true;
				autoBoardingFull = false;
			}
			if (manualBoarding)
				BoardManualCxl();
			else
				UpdateScreenMessages();
		}

		private bool SpaceAvail(Part p) {
			return (p.protoModuleCrew.Count < p.CrewCapacity);
		}

		private void CLSBoardAuto() {
			if (AirlockPlus.CLS.AllowUnrestrictedTransfers) {
				BoardAuto();
				return;
			}

			Log($"INFO: {vessel.vesselName} auto boarding {tgtAirlockPart.vessel.vesselName} via {tgtAirlockPart.partInfo.name}");

			ICLSSpace clsSpace = AirlockPlus.CLS.getCLSVessel(tgtAirlockPart.vessel).Parts.Find(x => x.Part == tgtAirlockPart).Space;

			// check in case of full vessel first
			if (clsSpace.Crew.Count >= clsSpace.MaxCrew) {
				Log("INFO: Auto boarding failed - CLS space full");
				ScreenMessages.PostScreenMessage(scrmsgCLSFull);

				// HACK: temporarily disable KerbalEVA for one update frame to prevent stock boarding from being registered alongside auto boarding
				// this prevents "spurious" stock "Cannot board a full module" message appearing alongside our auto boarding "Cannot board a full vessel"
				keva.enabled = false;
				autoBoardingFull = true;

				return;
			}

			// find part to board
			Part dest = null;
			if (SpaceAvail(tgtAirlockPart)) {
				// board the part itself if possible
				dest = tgtAirlockPart;
			} else {
				foreach (ICLSPart p in clsSpace.Parts) {
					if (SpaceAvail(p.Part)) {
						dest = p.Part;
						break;
					}
				}
			}
			if (dest == null) {
				Log($"ERROR: Auto boarding target vessel at {clsSpace.Crew.Count}/{clsSpace.MaxCrew} of CLS space capacity, but somehow unable to find a part with space?!");
				return;
			}

			keva.BoardPart(dest);
		}

		private void CLSBoardManualListParts() {
			if (AirlockPlus.CLS.AllowUnrestrictedTransfers) {
				BoardManualListParts();
				return;
			}

			Part p;
			foreach (ICLSPart clsp in AirlockPlus.CLS.getCLSVessel(tgtAirlockPart.vessel).Parts.Find(x => x.Part == tgtAirlockPart).Space.Parts) {
				p = clsp.Part;
				if (p.CrewCapacity>0) {
					highlightParts.Add(p.flightID,p);
					p.Highlight(false);
					p.SetHighlightType(Part.HighlightType.Disabled);
					p.highlighter.ConstantOn(SpaceAvail(p) ? COLOR_AVAIL : COLOR_FULL);
				}
			}
		}
		#endregion

		#region PartModule life cycle
		public override void OnLoad(ConfigNode node) {
			Log("INFO: Loading partmodule into " + part.name);

			// Sanity check: this module only applies to KerbalEVAs
			if (!part.Modules.Contains<KerbalEVA>()) {
				Log($"ERROR: {part.name} is not a KerbalEVA! Removing module.");
				part.RemoveModule(this);
			}
		}

		public override void OnStart(StartState state) {
			if (!HighLogic.LoadedSceneIsFlight)
				return;

			keva = part.Modules.GetModule<KerbalEVA>();

			// use keybinding from game settings
			boardkey = GameSettings.EVA_Board;
			scrmsgKeys.message = Localizer.Format("#autoLOC_AirlockPlusBP001",boardkey.primary.ToString());
			Log("INFO: EVA_Board key is " + boardkey.primary.ToString());

			GameEvents.onVesselChange.Add(OnVesselChange);
			MapView.OnEnterMapView += OnEnterMap;
			MapView.OnExitMapView += OnExitMap;

			// CLS support
			if (AirlockPlus.useCLS) {
				Log("INFO: CLS support enabled.");
				_BoardAuto = CLSBoardAuto;
				_BoardManualListParts = CLSBoardManualListParts;
			}
			else {
				_BoardAuto = BoardAuto;
				_BoardManualListParts = BoardManualListParts;
			}
		}

		private void OnDestroy() {
			GameEvents.onVesselChange.Remove(OnVesselChange);
			MapView.OnEnterMapView -= OnEnterMap;
			MapView.OnExitMapView -= OnExitMap;
		}
		#endregion

		#region Event Handling
		// Clean up after ourselves when active vessel changes, otherwise our screenmessages remain visible until expiry.
		private void OnVesselChange(Vessel v) {
			if (v != vessel)
				BoardCxl();
		}

		private void OnEnterMap() {
			inMap = true;
			BoardCxl();
		}
		private void OnExitMap() {
			inMap = false;
		}

		// in case the vessel we are in the midst of manual boarding has undergone unexpected modification...
		private void onVesselStandardModification(Vessel v) {
			if (v != tgtAirlockPart.vessel) return;
			Log("INFO: vessel being boarded has undergone unexpected modification!");
			BoardManualCxl();
			if (tgtAirlockPart == null) return;
			BoardManual();
		}
		#endregion

		private void Log(string s) {
			Debug.Log("[AirlockPlus|BoardingPass] " + s);
		}
	}
}
