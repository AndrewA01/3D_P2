using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class Submitter : MonoBehaviour
{
    string BASE_URL = "https://docs.google.com/forms/u/2/d/e/1FAIpQLSeiKdIJTaxhjXMkYweM2oLdKsVd2VgkD6nlZK0UA-4tQfKS-Q/formResponse";

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown("space"))
        {
            StartCoroutine(Post(transform.position.x, transform.position.y, transform.position.z));
        }
    }

    IEnumerator Post(float _x, float _y, float _z)
    {
        WWWForm form = new WWWForm();
        form.AddField("entry.1913558695", _x.ToString());
        form.AddField("entry.1543158515", _y.ToString());
        form.AddField("entry.2075670159", _z.ToString());
        byte[] rawData = form.data;

        using (UnityWebRequest www = UnityWebRequest.Post(BASE_URL, form))
        {
            yield return www.SendWebRequest();

            Debug.Log("Successfully sent POST request");

            if (www.isNetworkError || www.isHttpError)
            {
                Debug.Log(www.error);
            }
            else
            {
                Debug.Log(www.uploadHandler.data);
            }
        }
    }
}