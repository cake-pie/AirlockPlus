using System.Reflection;
using UnityEngine;
using Harmony;

namespace AirlockPlus.Harmony
{
	#region Patcher
	[KSPAddon(KSPAddon.Startup.Instantly, true)]
	public sealed class Patcher : MonoBehaviour
	{
		internal static HarmonyInstance harmony = HarmonyInstance.Create("com.github.cake-pie.AirlockPlus");
		private void Awake() {
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Destroy(gameObject);
		}
	}
	#endregion Patcher

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
}
