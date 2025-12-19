using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PupilLabs.Calibration
{
    using System.Net;
    using System.Net.Sockets;
    using System.Text;

    [Serializable]
    public class ParticipantResponse
    {
        public string id;
    }

    public class StudyServerConnector : MonoBehaviour
    {
        [Header("HTTP")]
        public string httpUrl;

        // wird nach erfolgreicher Discovery gesetzt
        public string BaseUrl { get; set; }

        public string LastParticipantId { get; private set; }

        public IEnumerator Connect(Action<string> onSuccess = null, Action<string> onError = null)
        {
            if (BaseUrl == null)
            {
                BaseUrl = httpUrl;
            }

            var url = $"{httpUrl}/ping";
            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
#else
if (www.isNetworkError || www.isHttpError)
#endif
            {
                Debug.LogError($"Ping failed: {www.error}");
                onError?.Invoke(www.error);
                yield break;
            }

            var response = www.downloadHandler.text;
            Debug.Log($"Ping response: {response}");

            yield return true;
        }

        public IEnumerator PostParticipant(Action<string> onSuccess = null, Action<string> onError = null)
        {
            if (BaseUrl == null)
            {
                Debug.LogError("BaseUrl is missing");
            }

            var url = $"{BaseUrl}/study/participant";

            // leerer Body, wie in deinem ursprünglichen Code
            UnityWebRequest www = UnityWebRequest.Post(url, new List<IMultipartFormSection>());

            yield return www.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (www.result != UnityWebRequest.Result.Success)
#else
        if (www.isNetworkError || www.isHttpError)
#endif
            {
                Debug.LogError($"PostParticipant failed: {www.error}");
                onError?.Invoke(www.error);
                yield break;
            }

            var json = www.downloadHandler.text;
            Debug.Log($"PostParticipant response: {json}");

            ParticipantResponse response = null;
            try
            {
                response = JsonUtility.FromJson<ParticipantResponse>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"JSON parse error: {e}");
                onError?.Invoke("JSON parse error");
                yield break;
            }

            if (response == null || string.IsNullOrEmpty(response.id))
            {
                const string parseError = "Invalid response from server (missing id)";
                Debug.LogError(parseError);
                onError?.Invoke(parseError);
                yield break;
            }

            // hier hast du die UUID vom Server
            LastParticipantId = response.id;
            onSuccess?.Invoke(response.id);
        }
    }
}
