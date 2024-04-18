using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DebugStats : MonoBehaviour
{

	[SerializeField] private TextMeshProUGUI text;
	[SerializeField] private float updateInterval = 1f;

    void Start()
    {
		UpdateText();
    }

	string debugStats()
	{
		float t = Time.deltaTime;
		float fr = 1 / t;
		int tris = GetComponent<CameraRaytracer>().Tris;
		return $"Δt: {t}\nFramerate: {fr}\nTris: {tris}";
	}

	void UpdateText()
	{
		text.text = debugStats();
		Invoke("UpdateText", updateInterval);
	}
}
