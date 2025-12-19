using PupilLabs.Serializable;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace PupilLabs.Calibration
{
    public class EyeTrackingCalibration : MonoBehaviour
    {
        [SerializeField]
        private GazeDataProvider gazeDataProvider;
        [SerializeField]
        private DataStorage storage;
        [SerializeField]
        private PoseSolver solver;
        [SerializeField]
        private Transform origin;
        [SerializeField]
        private Transform targets;
        [SerializeField]
        private TMPro.TextMeshProUGUI outTxt;
        [SerializeField]
        private GameObject outUi;

        [SerializeField] public StudyServerConnector studyServer;

        public DVector3Event calibrationFinished;

        private bool canSave = false;
        private Vector3 solvedPosition = Vector3.zero;
        private Quaternion solvedRotation = Quaternion.identity;
        private int currentTargetId = 0;
        private CalibrationTarget[] calibrationTargets = null;

        private void Start()
        {
            StartCoroutine(StartCalibration());
        }

        private IEnumerator StartCalibration()
        {
            outUi.SetActive(false);

            if (gazeDataProvider == null)
            {
                gazeDataProvider = ServiceLocator.Instance.GazeDataProvider;
            }
            if (storage == null)
            {
                storage = ServiceLocator.Instance.GetComponentInChildren<DataStorage>(true);
            }
            calibrationFinished.AddListener(gazeDataProvider.SetGazeOrigin);
            calibrationTargets = targets.GetComponentsInChildren<CalibrationTarget>();

            foreach (CalibrationTarget t in calibrationTargets)
            {
                t.gameObject.SetActive(false);
            }
            calibrationTargets[currentTargetId].gameObject.SetActive(true);

            yield return StartCoroutine(studyServer.Connect(result =>
                {
                },
                error =>
                {
                    Debug.Log($"[StudyServer] Error: {error}");
                })
            );

            yield return StartCoroutine(studyServer.PostParticipant(uuid =>
                {
                    Debug.Log($"[StudyServer] New Participant: {uuid}");
                },
                error =>
                {
                    Debug.LogError($"[StudyServer] Error: {error}");
                })
            );
        }

        private IEnumerator ShowNextRoutine()
        {
            yield return new WaitUntil(() => calibrationTargets[currentTargetId] == null); //wait until current target is destroyed
            calibrationTargets[++currentTargetId].gameObject.SetActive(true); //move to next target
        }

        public async Task Solve()
        {
            canSave = false;
            solver.SetVisualizationActive(true); //prior to solve just to see live update of iterative kabsch
            await solver.Solve();
            solvedPosition = solver.Solution.GetPosition();
            solvedRotation = solver.Solution.rotation;
            canSave = true;

            outTxt.SetText($"Pos: {solvedPosition.x}, {solvedPosition.y}, {solvedPosition.z}<br>Rot: {solvedRotation.eulerAngles.x}, {solvedRotation.eulerAngles.y}, {solvedRotation.eulerAngles.z}");
            outUi.SetActive(true);
            calibrationFinished.Invoke(solvedPosition, solvedRotation.eulerAngles);
        }

        public void AddObservation(Vector3 worldPos)
        {
            if (currentTargetId != solver.SampleCount) //ignore multiple submits of the same target
            {
                return;
            }

            var localPos = origin.InverseTransformPoint(worldPos); //local in origin space
            var gazeDir = gazeDataProvider.RawGazeDir; //local in sensor space
            solver.AddSample(localPos, gazeDir);

            Debug.Log($"[EyeTrackingCalibration] adding observation - worldpos: {worldPos} localpos: {localPos} gaze dir: {gazeDir}");

            if (solver.SampleCount == calibrationTargets.Length)
            {
                Solve().Forget();
            }
            else
            {
                StartCoroutine(ShowNextRoutine());
            }
        }

        public void TriggerSave()
        {
            StartCoroutine(SaveRoutine());
        }

        public void TriggerResetDefaults()
        {
            ResetDefaults().Forget();
        }

        private IEnumerator SaveRoutine()
        {
            if (!canSave) yield break;
            canSave = false;

            var whenReady = storage.WhenReady();
            while (!whenReady.IsCompleted) yield return null;

            AppConfig config = storage.Config;
            config.sensorCalibration.offset.position.x = solvedPosition.x;
            config.sensorCalibration.offset.position.y = solvedPosition.y;
            config.sensorCalibration.offset.position.z = solvedPosition.z;
            config.sensorCalibration.offset.rotation.x = solvedRotation.eulerAngles.x;
            config.sensorCalibration.offset.rotation.y = solvedRotation.eulerAngles.y;
            config.sensorCalibration.offset.rotation.z = solvedRotation.eulerAngles.z;

            var writeTask = File.WriteAllTextAsync(storage.ConfigFilePath, JsonUtility.ToJson(storage.Config, true));
            while (!writeTask.IsCompleted) yield return null;

            var msg = "Calibration uploaded to Study-Server";
            yield return StartCoroutine(studyServer.PutFile(storage,
                success => { },
                error =>
                {
                    msg = error;
                    Debug.LogError($"[StudyServer] Error: {error}");
                }));

            outTxt.SetText(msg);
            canSave = true;
        }

        public async Task Save()
        {
            if (canSave == false)
            {
                return;
            }
            canSave = false;

            await storage.WhenReady();

            AppConfig config = storage.Config;
            config.sensorCalibration.offset.position.x = solvedPosition.x;
            config.sensorCalibration.offset.position.y = solvedPosition.y;
            config.sensorCalibration.offset.position.z = solvedPosition.z;
            config.sensorCalibration.offset.rotation.x = solvedRotation.eulerAngles.x;
            config.sensorCalibration.offset.rotation.y = solvedRotation.eulerAngles.y;
            config.sensorCalibration.offset.rotation.z = solvedRotation.eulerAngles.z;

            await File.WriteAllTextAsync(storage.ConfigFilePath, JsonUtility.ToJson(config, true));

            var msg = "hallo";
            StartCoroutine(studyServer.PutFile(storage, success => { }, error =>
            {
                msg = error;
                Debug.LogError($"[StudyServer] Error: {error}");
            }));

            //outTxt.SetText($"Saved to: {storage.ConfigFilePath}");
            //outTxt.SetText($"Calibration uploaded to Study-Server");
            outTxt.SetText(msg);

            canSave = true;
        }

        public async Task ResetDefaults()
        {
            if (canSave == false)
            {
                return;
            }
            canSave = false;

            await storage.WhenReady();
            AppConfig configDefaults = storage.ConfigDefaults;

            var pos = configDefaults.sensorCalibration.offset.position;
            var rot = configDefaults.sensorCalibration.offset.rotation;

            solvedPosition = new Vector3(pos.x, pos.y, pos.z);
            solvedRotation.eulerAngles = new Vector3(rot.x, rot.y, rot.z);

            outTxt.SetText($"Pos: {pos.x}, {pos.y}, {pos.z}<br>Rot: {rot.x}, {rot.y}, {rot.z}");
            canSave = true;

            calibrationFinished.Invoke(new Vector3(pos.x, pos.y, pos.z), new Vector3(rot.x, rot.y, rot.z));
        }
    }
}
