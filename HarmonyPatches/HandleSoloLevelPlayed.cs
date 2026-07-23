using HarmonyLib;
using SquatToBegin.GameLogic;

namespace SquatToBegin.HarmonyPatches {
	[HarmonyPatch(typeof(SinglePlayerLevelSelectionFlowCoordinator), nameof(SinglePlayerLevelSelectionFlowCoordinator.StartLevel))]
	static class HandleSoloLevelPlayed {
		static void Prefix(bool practice) {
			// Fresh starts from song select / results restart must not inherit a
			// skip flag left over from an in-map restart with EnableAfterRestart off.
			SquatChecker.MarkFreshLevelStart();

			if(practice && !Config.Instance.EnableInPractice)
				return;

			SquatChecker.enableOnNextSong = true;
		}
	}
}
