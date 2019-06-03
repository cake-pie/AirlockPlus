// © 2017-2019 cake>pie
// All rights reserved

using System.Reflection;
using UnityEngine;
using KSP.UI.Screens.Flight.Dialogs;
using Harmony;

namespace AirlockPlus.Harmony
{
	#region Patcher
	[KSPAddon(KSPAddon.Startup.Instantly, true)]
	public sealed class Patcher : MonoBehaviour
	{
		internal static bool harmonyAvailable = false;

		private void Awake() {
			// If Harmony dependency is missing, AddonLoader will barf in logs when instantiating addon
			//     ADDON BINDER: Cannot resolve assembly ...
			// and none of this will execute
			Assembly harmonyAssy = typeof(HarmonyInstance).Assembly;
			Debug.Log($"[AirlockPlus|Patcher] Using Harmony {harmonyAssy.GetName().Version.ToString()} located at {harmonyAssy.Location}");
			HarmonyInstance harmony = HarmonyInstance.Create("com.github.cake-pie.AirlockPlus");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			harmonyAvailable = true;
			Destroy(gameObject);
		}
	}
	#endregion Patcher

	#region CrewHatchDialog
	// https://kerbalspaceprogram.com/api/class_k_s_p_1_1_u_i_1_1_screens_1_1_flight_1_1_dialogs_1_1_crew_hatch_dialog.html

	// It seems to take stock KSP an inconsistent amount of time to instantiate and populate the CrewHatchDialog
	// We need to wait for stock KSP to finish populating the CrewHatchDialog before augmenting or hijacking it
	// otherwise it will overwrite our stuff (instead of the other way round)
	// KSP API provides no formal indication for this (there are GameEvents for a couple other UI dialogs)
	// Use a postfix to provide notification that dialog has been populated

	// protected void CreatePanelContent()
	[HarmonyPatch(typeof(CrewHatchDialog))]
	[HarmonyPatch("CreatePanelContent")]
	internal class CrewHatchDialog_CreatePanelContent
	{
		[HarmonyPostfix]
		private static void Postfix(CrewHatchDialog __instance) {
			if (!HighLogic.LoadedSceneIsFlight || AirlockPlus.Instance == null)
				return;
			AirlockPlus.Instance.OnCHDReady(__instance);
		}
	}

	// We then also need a way to know in case CrewHatchDialog spawn/populate is aborted before it is ready
	// Also useful for cleanup when CrewHatchDialog closed regardless of whether any kerbal went EVA

	// protected void Terminate()
	[HarmonyPatch(typeof(CrewHatchDialog))]
	[HarmonyPatch("Terminate")]
	internal class CrewHatchDialog_Terminate
	{
		[HarmonyPostfix]
		private static void Postfix(CrewHatchDialog __instance) {
			if (!HighLogic.LoadedSceneIsFlight || AirlockPlus.Instance == null)
				return;
			AirlockPlus.Instance.OnCHDTerminated(__instance);
		}
	}
	#endregion CrewHatchDialog

	#region FlightEVA
	// https://kerbalspaceprogram.com/api/class_flight_e_v_a.html

	// Stock KSP only supports going EVA with a kerbal that is inside the part that has the airlock
	// From black box testing and deduction based on API, FlightEVA.spawnEVA expects
	// - fromPart to contain pCrew
	//   - tests pCrew inside fromPart, balks and logs error if condition not satisfied
	//     "[FlightEVA]: Tried to bail out <crew name> from part <part name> but part doesn't contain that kerbal!"
	// - fromPart to be the part that has the airlock fromAirlock
	//   - used in hatch obstructed tests
	//   - can lead to strange/incorrect behavior if something else other than expected
	//     https://github.com/cake-pie/AirlockPlus/issues/1
	//     https://github.com/cake-pie/AirlockPlus/issues/12
	// a084ce2 tried passing fromPart containing pCrew but doctored with airlock part position
	// - this was an incredibly nasty hack and could have been cause of strange behavior
	//   https://github.com/cake-pie/AirlockPlus/issues/11
	// Using Harmony allows targeted patching to feed the hatch obstructed tests the correct information
	// (i.e. airlock part) in a less hacky fashion with hopefully less unintended side effects

	// public static bool HatchIsObstructed(Part fromPart, Transform fromAirlock)
	[HarmonyPatch(typeof(FlightEVA))]
	[HarmonyPatch("HatchIsObstructed")]
	internal class FlightEVA_HatchIsObstructed
	{
		[HarmonyPrefix]
		private static void Prefix(ref Part fromPart, Transform fromAirlock) {
			if (!HighLogic.LoadedSceneIsFlight || AirlockPlus.Instance == null)
				return;
			if (fromPart == AirlockPlus.Instance.kerbalPart &&
				fromAirlock == AirlockPlus.Instance.airlock.transform)
				fromPart = AirlockPlus.Instance.airlockPart;
		}
	}

	// public static bool HatchIsObstructedMore(Part fromPart, Transform fromAirlock)
	// Same as HatchIsObstructed but raytraces inwards and with a OverlapSphere to catch more cases.
	[HarmonyPatch(typeof(FlightEVA))]
	[HarmonyPatch("HatchIsObstructedMore")]
	internal class FlightEVA_HatchIsObstructedMore
	{
		[HarmonyPrefix]
		private static void Prefix(ref Part fromPart, Transform fromAirlock) {
			if (!HighLogic.LoadedSceneIsFlight || AirlockPlus.Instance == null)
				return;
			if (fromPart == AirlockPlus.Instance.kerbalPart &&
				fromAirlock == AirlockPlus.Instance.airlock.transform)
				fromPart = AirlockPlus.Instance.airlockPart;
		}
	}

	// public static bool hatchInsideFairing(Part fromPart)
	// Checks if fromPart if inside a fairing that is NOT an interstage fairing, as we
	// don't want to allow EVAs from inside a fairing unless it is an interstage fairing.
	[HarmonyPatch(typeof(FlightEVA))]
	[HarmonyPatch("hatchInsideFairing")]
	internal class FlightEVA_hatchInsideFairing
	{
		[HarmonyPrefix]
		private static void Prefix(ref Part fromPart) {
			if (!HighLogic.LoadedSceneIsFlight || AirlockPlus.Instance == null)
				return;
			if (fromPart == AirlockPlus.Instance.kerbalPart)
				fromPart = AirlockPlus.Instance.airlockPart;
		}
	}
	#endregion FlightEVA

	#region KerbalEVA
	// https://kerbalspaceprogram.com/api/class_kerbal_e_v_a.html

	// BoardingPass needs to turn off stock command hints while in manual boarding mode, which proved tricky.
	// Stock KSP code apparently spams ScreenMessages.PostScreenMessage() on *every frame* to display its command hints.
	// These options will not work:
	// a) ScreenMessages.Instance.enabled = false;
	//     - No-go, because I need to show my own ScreenMessages at the same time.
	// b) part.Modules.GetModule<KerbalEVA>().enabled = false;
	//     - This turns kerbal into a drifting brick because KerbalEVA is responsible for maintaining correct position
	//       and orientation of the EVA kerbal part/vessel with respect to world space.
	// One disgusting but functional workaround was to run code every frame in LateUpdate (OnUpdate didn't work!) which
	// iterates over ScreenMessages.Instance.ActiveMessages and remove all messages in LOWER_CENTER position
	// This approach is so much better, we can get KerbalEVA to STFU while manual boarding is active.

	// protected virtual void PostInteractionScreenMessage(string message, float delay=0.1f)
	[HarmonyPatch(typeof(KerbalEVA))]
	[HarmonyPatch("PostInteractionScreenMessage")]
	internal class KerbalEVA_PostInteractionScreenMessage
	{
		[HarmonyPrefix]
		private static bool Prefix(KerbalEVA __instance) {
			if (__instance.part.Modules.GetModule<BoardingPass>()?.manualBoarding ?? false)
				return false;
			return true;
		}
	}

	// When auto-boarding in BoardingPass encounters a completely full vessel, it displays a screen message and quits.
	// This leaves it open for stock KSP code to capture the same "B" keypress (from Shift+B) and attempt boarding also
	// leading to additional screen message (#autoLOC_115954 = Cannot board a full module.) which we want to suppress.
	// Previously this was done by outright disabling KerbalEVA for one frame, a slightly risky hack.
	// Silencing PostInteractionScreenMessage() does not help, the screenmessage seems to emanate from BoardPart()

	// public virtual void BoardPart (Part p)
	[HarmonyPatch(typeof(KerbalEVA))]
	[HarmonyPatch("BoardPart")]
	internal class KerbalEVA_BoardPart
	{
		[HarmonyPrefix]
		private static bool Prefix(KerbalEVA __instance) {
			if (__instance.part.Modules.GetModule<BoardingPass>()?.autoBoardingFull ?? false)
				return false;
			return true;
		}
	}
	#endregion KerbalEVA
}
