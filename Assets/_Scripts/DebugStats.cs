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

	/// <summary>
	/// lists previous frame's delta time and the current framerate
	/// </summary>
	/// <returns>string of debug info</returns>
	string debugStats()
	{
		float t = Time.deltaTime;
		float fr = 1 / t;
		return $"Δt: {t}\nFramerate: {fr}";
	}

	void UpdateText()
	{
		text.text = debugStats();
		Invoke("UpdateText", updateInterval);
	}
}
