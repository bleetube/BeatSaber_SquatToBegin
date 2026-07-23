using System;
using System.Linq;
using System.Threading.Tasks;
using SiraUtil.Tools.FPFC;
using UnityEngine;
using Zenject;

namespace SquatToBegin.GameLogic {
	class SquatChecker : ILateTickable, IDisposable, IInitializable {
		public static bool enableOnNextSong = false;
		static int forcedSquatsOnNextStart = 0;
		/// <summary>
		/// Set when restarting with EnableAfterRestart disabled so the next
		/// StandardPlayer install skips arming the squat gate.
		/// </summary>
		static bool skipGateOnNextStart = false;

		readonly AudioTimeSyncController atsc;
		readonly PauseMenuManager pauseMenuManager;
		readonly GameplayCoreSceneSetupData gameplayCoreSceneSetupData;

		public bool allowPlay { get; private set; } = false;
		public bool isActive { get; private set; } = false;

		readonly Instructor instructor;

		Camera headCamera;

		public SquatChecker(
			AudioTimeSyncController atsc,
			GameplayCoreSceneSetupData gameplayCoreSceneSetupData,
			PlayerTransforms playerTransforms,
			PauseMenuManager pauseMenuManager,
			IFPFCSettings FPFCSettings,
			Instructor instructor
		) {
#if !DEBUG
			if(FPFCSettings.Enabled) {
				allowPlay = true;
				return;
			}
#endif

			this.pauseMenuManager = pauseMenuManager;
			this.atsc = atsc;
			this.gameplayCoreSceneSetupData = gameplayCoreSceneSetupData;

			this.instructor = instructor;

			pauseMenuManager.didPressRestartButtonEvent += PauseMenuManager_didPressRestartButtonEvent;

			if(!ShouldSquat())
				return;

			enableOnNextSong = false;
			forcedSquatsOnNextStart = 0;
		}

		public static void MarkFreshLevelStart() {
			skipGateOnNextStart = false;
		}

		public bool ShouldSquat(bool enableOverride = false) {
			// Pause-menu restart with EnableAfterRestart disabled: do not re-arm.
			// The installed 1.44 build always-arms on StandardPlayer construct and
			// stopped consulting enableOnNextSong, so EnableAfterRestart had no effect
			// unless we carry an explicit skip across the scene replace.
			if(!enableOverride && skipGateOnNextStart && forcedSquatsOnNextStart < 1) {
				skipGateOnNextStart = false;
				allowPlay = true;
				Plugin.Log.Debug("Squat gate skipped after restart (EnableAfterRestart disabled).");
				return false;
			}

			if(!enableOverride && forcedSquatsOnNextStart < 1 && gameplayCoreSceneSetupData.practiceSettings != null && !Config.Instance.EnableInPractice) {
				allowPlay = true;
				Plugin.Log.Debug("Squat gate skipped for practice mode.");
				return false;
			}

			if(forcedSquatsOnNextStart < 1 && Plugin.rng.NextDouble() >= Config.Instance.Chance) {
				allowPlay = true;
				Plugin.Log.Debug("Squat gate skipped by configured chance.");
				return false;
			}

			squatsNeeded = forcedSquatsOnNextStart != 0 ?
				forcedSquatsOnNextStart : Math.Max(Config.Instance.SquatsNeeded, forcedSquatsOnNextStart);

			allowPlay = false;
			Plugin.Log.Info(string.Format("Squat gate armed; waiting for {0} squat{1}.", squatsNeeded, squatsNeeded == 1 ? "" : "s"));

			return true;
		}

		public void Dispose() {
			pauseMenuManager.didPressRestartButtonEvent -= PauseMenuManager_didPressRestartButtonEvent;
		}

		private void PauseMenuManager_didPressRestartButtonEvent() {
			if(!Config.Instance.EnableAfterRestart && squatsNeeded <= 0) {
				skipGateOnNextStart = true;
				enableOnNextSong = false;
				forcedSquatsOnNextStart = 0;
				Plugin.Log.Debug("Restart with EnableAfterRestart disabled; next start will skip squat gate.");
				return;
			}

			skipGateOnNextStart = false;
			enableOnNextSong = true;
			forcedSquatsOnNextStart = squatsNeeded;
		}

		int squatsNeeded = 0;

		float standingHeight = 0;
		float targetHeight = 0;

		bool isUnsquatted = true;

		Action finishCallback;
		public void SetFinishCallback(Action callback) {
			finishCallback = callback;
		}

		public void Initialize() => FindTheCamera();

		bool FindTheCamera() {
			headCamera = UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(x => x.stereoEnabled && x.isActiveAndEnabled);

			return isActive = headCamera != null;
		}

		public void LateTick() {
			if(atsc == null)
				return;

#if !DEBUG
			if(headCamera == null) {
				isActive = false;
				return;
			}

			if(!headCamera.stereoEnabled || !headCamera.isActiveAndEnabled) {
				targetHeight = 0;
				if(!FindTheCamera())
					return;
			}
#endif

			if(!allowPlay) {
				if(atsc.state == IAudioTimeSource.State.Playing) {
					atsc.Pause();

					instructor.Show();
					instructor.SetText(squatsNeeded);
					instructor.PlaySound();
				}
			} else if(!Config.Instance.CountSquatsDoneMidLevel) {
				return;
			}

			float p;

#if DEBUG
			if(Input.GetKeyDown(KeyCode.Space)) {
				p = -420;
			} else {
				p = 420;
			}
#else
			p = headCamera.transform.localPosition.y;

			if(p == 0)
				return;
#endif

			if(targetHeight == 0) {
				standingHeight = p - Math.Max(0.1f, Config.Instance.SquatAmount * .25f);
				targetHeight = p - Config.Instance.SquatAmount;
			}

			if(p >= standingHeight)
				isUnsquatted = true;

			if(isUnsquatted && p < targetHeight) {
				instructor.ConfirmSquat();

				if(squatsNeeded > 0) {
					if(--squatsNeeded == 0) {
						instructor.Hide();

						void exit() {
							finishCallback?.Invoke();
							finishCallback = null;

							allowPlay = true;
							atsc.Resume();
						}
						if(Config.Instance.UnpauseDelay > 0) {
							Task.Delay((int)(Config.Instance.UnpauseDelay * 100)).ContinueWith(x => {
								exit();
							}, TaskScheduler.FromCurrentSynchronizationContext());
						} else {
							exit();
						}
					} else {
						instructor.SetText(squatsNeeded);
					}
				}

				isUnsquatted = false;
			}
		}
	}
}
