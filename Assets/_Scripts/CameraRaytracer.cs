using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class CameraRaytracer : MonoBehaviour
{

	private RenderTexture rt;
	[SerializeField] private ComputeShader rayTracingShader;
	Camera cam;

	[SerializeField] private bool sunLight = true;
	[SerializeField] private Light sun;
	[SerializeField] int sphereCount = 20;
	ComputeBuffer sphereBuffer;

	private void OnEnable()
	{
		
		List<Sphere> spheres = new List<Sphere>();
		for (int i = 0; i < sphereCount; i++)
		{
			Vector3 position = Random.insideUnitSphere * 25;
			position = new Vector3(position.x,1,position.z);
			Sphere testBall = makeSphere(position, Random.Range(1,3), Random.insideUnitSphere, Random.value);
			spheres.Add(testBall);
		}
		sphereBuffer = new ComputeBuffer(sphereCount, 13 * sizeof(float));
		sphereBuffer.SetData(spheres);
	}

	private void OnDisable()
	{
		if (sphereBuffer != null)
			sphereBuffer.Release();
	}

	// Start is called before the first frame update
	void Awake()
    {
        cam = GetComponent<Camera>();
    }

	private void OnRenderImage(RenderTexture source, RenderTexture destination)
	{
		SetShaderVars();
		Render(destination);
	}

	// Update is called once per frame
	void Update()
	{
		sunLight = Input.GetKeyDown(KeyCode.Alpha1) ? !sunLight : sunLight;
    }

	void SetShaderVars()
	{
		rayTracingShader.SetMatrix("CameraToWorldMat", cam.cameraToWorldMatrix);
		rayTracingShader.SetMatrix("InversePerspectiveProjMat", cam.projectionMatrix.inverse);
		Vector3 l = sun.transform.forward;
		rayTracingShader.SetVector("Sun", new Vector4(l.x, l.y, l.z, sun.intensity));
		rayTracingShader.SetBuffer(0,"spheres",sphereBuffer);
		rayTracingShader.SetBool("sunLight", sunLight);
	}

	void Render(RenderTexture destination)
	{
		InitRenderTexture();

		rayTracingShader.SetTexture(0, "Result", rt);
		int threadGroupsX = Mathf.CeilToInt(Screen.width / 8.0f);
		int threadGroupsY = Mathf.CeilToInt(Screen.height / 8.0f);
		rayTracingShader.Dispatch(0,threadGroupsX,threadGroupsY,1);
		Graphics.Blit(rt, destination);
	}

	/// <summary>
	/// Creates the render texture which is sent to the GPU and is displayed on the screen
	/// </summary>
	void InitRenderTexture()
	{
		if(rt == null || rt.width != Screen.width || rt.height != Screen.height)
		{
			if(rt != null)
				rt.Release();

			rt = new RenderTexture(Screen.width, Screen.height, 0,
				RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
			rt.enableRandomWrite = true;
			rt.Create();
		}
	}

	Sphere makeSphere(Vector3 position, float radius, Vector3 albedo, float smoothness)
	{
		Sphere sphere = new Sphere();
		sphere.position = position;
		sphere.radius = radius;
		sphere.material.albedo = albedo;
		sphere.material.smoothness = smoothness;
		sphere.material.emission = Vector3.zero;
		sphere.material.emissionStrength = 0.0f;
		sphere.material.metalic = 0.0f;
		return sphere;
	}
}

struct PMaterial
{
	public Vector3 albedo;
	public float smoothness;
	public float metalic;
	public Vector3 emission;
	public float emissionStrength;
}

struct Sphere
{
	public Vector3 position;
	public float radius;
	public PMaterial material;
}