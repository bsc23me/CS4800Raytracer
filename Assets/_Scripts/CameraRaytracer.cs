using JetBrains.Annotations;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class CameraRaytracer : MonoBehaviour
{

	public const int MaterialSize = 9;
	public const int TriangleSize = 7;
	public const int SphereSize = 4 + MaterialSize;

	private RenderTexture rt;
	[SerializeField] private ComputeShader rayTracingShader;

	[SerializeField] private int TILE_SIZE = 8;
	Camera cam;

	[SerializeField] private bool sunLight = true;
	[SerializeField] private Light sun;
	[SerializeField] int sphereCount = 20;
	[SerializeField] bool rotate = true;
	ComputeBuffer sphereBuffer;
	ComputeBuffer vertexBuffer;
	ComputeBuffer normalBuffer;
	ComputeBuffer UVBuffer;
	ComputeBuffer meshBuffer;

	List<PMesh> meshes;
	List<Vector3> staticVertices;
	List<Vector3> staticNormals;
	List<Vector2> staticUVs;
	List<Triangle> staticTriangles;

	[SerializeField] Texture2D texture;
	[SerializeField] Texture2D normalMap;

	private PMaterial blueMat = makeMat(Vector3.forward, 0f);
	private PMaterial mirrorMat = makeMat(Vector3.one, 1f);
	private PMaterial metalMat = makeMat(Vector3.one, 0.9f);
	private PMaterial goldMat = makeMat(new Vector3(1,1,0), 1f);

	[SerializeField, Range(0,3)] private int scene = 0;

	private void OnEnable()
	{

		List<Sphere> spheres = new List<Sphere>();
		for (int i = 0; i < sphereCount; i++)
		{
			Vector3 position = UnityEngine.Random.insideUnitSphere * 25;
			position = new Vector3(position.x,1,position.z);
			Sphere testBall = makeSphere(position, UnityEngine.Random.Range(1,3), UnityEngine.Random.insideUnitSphere, UnityEngine.Random.value);
			spheres.Add(testBall);
		}
		sphereBuffer = new ComputeBuffer(sphereCount, SphereSize * sizeof(float));
		sphereBuffer.SetData(spheres);

		// Red Square for posterity
		PMesh pMesh = new PMesh();
		pMesh.verts = new Vector3[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up };
		int[] f1 = new int[] { 0, 1, 2 };
		int[] f2 = new int[] { 2, 3, 0 };
		pMesh.faces = new int[][] { f1, f2 };

		staticVertices = new List<Vector3>();
		staticNormals = new List<Vector3>();
		staticUVs = new List<Vector2>();
		staticTriangles = new List<Triangle>();

		LoadScene(scene);

		/*staticVertices.AddRange(pMesh.verts);
		staticNormals.Add(Vector3.back);

		Triangle tri1 = new Triangle();
		tri1.points.x = 5;
		tri1.points.y = 3;
		tri1.points.z = 1;
		tri1.normal = 0;
		Triangle tri2 = new Triangle();
		tri2.points.x = 2;
		tri2.points.y = 3;
		tri2.points.z = 0;
		tri2.normal = 0;

		staticTriangles.Add(tri1);
		staticTriangles.Add(tri2);
		*/
		//LoadScene(scene);


		int dyn = 0;
		/*foreach (PMesh mesh in meshes)
			if(mesh.rotationSpeed == 0)
				staticTriangles.AddRange(Triangulate(mesh));
			else
				dyn += mesh.faces.GetLength(0);*/
		
		vertexBuffer = new ComputeBuffer(staticVertices.Count + dyn, 3 * sizeof(float));
		normalBuffer = new ComputeBuffer(staticNormals.Count + dyn, 3 * sizeof(float));
		UVBuffer = new ComputeBuffer(staticUVs.Count + dyn, 2 * sizeof(float));
		meshBuffer = new ComputeBuffer(staticTriangles.Count + dyn, TriangleSize * sizeof(float));

		vertexBuffer.SetData(staticVertices);
		normalBuffer.SetData(staticNormals);
		UVBuffer.SetData(staticUVs);
		meshBuffer.SetData(staticTriangles);

		Debug.Log("");
	}

	private void OnDisable()
	{
		if (sphereBuffer != null)
			sphereBuffer.Release();
		if (vertexBuffer != null)
			vertexBuffer.Release();
		if (normalBuffer != null)
			normalBuffer.Release();
		if (meshBuffer != null)
			meshBuffer.Release();
		if (UVBuffer != null)
			UVBuffer.Release();
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
		Time.timeScale = Input.GetKey(KeyCode.Alpha2) ? 0 : 1;

		//sun.transform.eulerAngles += Vector3.right * Time.deltaTime;

    }

	/*private void FixedUpdate()
	{
		List<Triangle> triangles = new List<Triangle>();
		foreach (PMesh mesh in meshes)
			if(mesh.rotationSpeed != 0)
				triangles.AddRange(Triangulate(mesh));
		triangles.AddRange(staticTriangles);
		meshBuffer.SetData(triangles);
	}
	*/
	void SetShaderVars()
	{
		rayTracingShader.SetMatrix("CameraToWorldMat", cam.cameraToWorldMatrix);
		rayTracingShader.SetMatrix("InversePerspectiveProjMat", cam.projectionMatrix.inverse);

		Vector3 l = sun.transform.forward;
		rayTracingShader.SetVector("Sun", new Vector4(l.x, l.y, l.z, sun.intensity));
		rayTracingShader.SetBool("sunLight", sunLight);

		//rayTracingShader.SetBuffer(0,"spheres",sphereBuffer);

		rayTracingShader.SetBuffer(0, "Vertices", vertexBuffer);
		rayTracingShader.SetBuffer(0, "VNormals", normalBuffer);
		rayTracingShader.SetBuffer(0, "UVs", UVBuffer);
		rayTracingShader.SetTexture(0, "Tex", texture);
		rayTracingShader.SetTexture(0, "Norm", normalMap);
		rayTracingShader.SetBuffer(0,"Meshes", meshBuffer);

		rayTracingShader.SetInt("SAMPLES", 1/*scene == 0 ? 1 : 32*/);

		
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

	#region structure constructors

	static PMaterial makeMat(Vector3 albedo, float smoothness)
	{
		PMaterial material = new PMaterial();
		material.albedo = albedo;
		material.smoothness = smoothness;
		return material;
	}

	PMaterial randMat()
	{
		return makeMat(UnityEngine.Random.insideUnitSphere.normalized, 0f);
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
	/*
	Triangle makeTri(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, PMaterial material)
	{
		Triangle tri = new Triangle();
		tri.a = a;
		tri.b = b;
		tri.c = c;
		tri.normal = normal;
		tri.material = material;
		return tri;
	}*/


	/*
	Triangle transformVert(Vector3 vertex, Vector3 position, float3 rotation, float scale)
	{
		
		float x, y, z;
		Vector3[] rotX = new Vector3[] {
			new Vector3(0, Mathf.Cos(rotation), -Mathf.Sin(rotation.x)),
			new Vector3(0, Mathf.Sin(rotation), Mathf.Cos(rotation.x)) };

		Vector3[] rotY = new Vector3[] {
			new Vector3(Mathf.Sin(rotation), 0, Mathf.Cos(rotation.y)) ,
			new Vector3(Mathf.Cos(rotation), 0, -Mathf.Sin(rotation.y))};

		Vector3[] rotZ = new Vector3[] {
			new Vector3(Mathf.Cos(rotation), -Mathf.Sin(rotation.z), 0),
			new Vector3(Mathf.Sin(rotation), Mathf.Cos(rotation.z), 0) };
		
		x = Vector3.Dot(vertex, rotY[0]);
		y = vertex.y;
		z = Vector3.Dot(vertex, rotY[1]);
		ret = new Vector3(x, y, z);

		x = Vector3.Dot(staticVertices[tri.points.y], rot[0]);
		y = Vector3.Dot(staticVertices[tri.points.y], rot[1]);
		z = staticVertices[tri.points.y].z;
		t.b = new Vector3(x, y, z);

		x = Vector3.Dot(staticVertices[tri.points.z], rot[0]);
		y = Vector3.Dot(staticVertices[tri.points.z], rot[1]);
		z = tri.c.z;
		t.c = new Vector3(x, y, z);

		x = Vector3.Dot(tri.normal, rot[0]);
		y = Vector3.Dot(tri.normal, rot[1]);
		z = tri.normal.z;
		t.normal = new Vector3(x, y, z);

		t.a *= scale;
		t.b *= scale;
		t.c *= scale;

		t.a += position;
		t.b += position;
		t.c += position;
		return t;
	}
	*/
	#endregion

	#region Mesh IO

	List<PMesh> LoadScene(int scene)
	{
		List<PMesh> meshes = new List<PMesh>();
		string path = "Assets/Models/";
		string[] models = new string[] { path + "CubeT.obj", path + "SphereI.obj", path + "Mug.obj" , path + "WoodPlank.obj"};

		LoadMesh(models[scene], randMat(), Vector3.zero, 0);

		if (scene == 0)
		{

			/*for (int i = 0; i < 8; i++)
			{
				Vector3 pos = Random.insideUnitSphere * 10;
				pos.y = 0;
				PMaterial material = i % 2 == 0 ? mirrorMat : randMat();
				meshes.Add(LoadMesh(models[i%models.Length], material, pos, i % 4 == 0 ? i % 8 == 0 ? 0.5f : -1 : 0));
			}

			tris.Add(transformTri(makeTri(
				Vector3.down,
				new Vector3(0.425323f, -0.850654f, 0.309011f),
				new Vector3(-0.162456f, -0.850654f, 0.5f),
				new Vector3(0.1024f, -0.9435f, 0.3151f),
				makeMat(Vector3.forward, 0.5f)), new Vector3(2,1,3),Time.time,1));*/
		}
		else if (scene == 1)
		{
			//meshes = new List<PMesh>() { LoadMesh("Assets/Models/CubeT.obj", metalMat, Vector3.zero, 0f)};
		}
		return meshes;
	}

	PMesh LoadMesh(string path, PMaterial material, Vector3 position, float rotation)
	{
		StreamReader streamReader = new StreamReader(path);
		PMesh mesh = new PMesh();
		mesh.material = material;
		mesh.position = position;
		mesh.rotationSpeed = rotation;

		List<Vector3> vertices = new List<Vector3>();
		List<Vector3> vertexNormals = new List<Vector3>();
		List<int[]> faces = new List<int[]>();

		string data = streamReader.ReadToEnd();
		string[] lines = data.Split("\n");
		for(int i = 0; i < lines.Length; i++)
		{
			if (lines[i].StartsWith("v "))
			{
				staticVertices.Add(LoadVector(lines[i]));
			}
			else if (lines[i].StartsWith("vt"))
			{
				staticUVs.Add(LoadVector2(lines[i]));
			}
			else if (lines[i].StartsWith("vn"))
			{
				staticNormals.Add(LoadVector(lines[i]));
			}
			else if (lines[i].StartsWith("f "))
			{
				staticTriangles.Add(LoadFace(lines[i]));
			}
		}
		mesh.verts = vertices.ToArray();
		mesh.normals = vertexNormals.ToArray();
		mesh.faces = faces.ToArray();
		return mesh;
	}

	Vector3 LoadVector(string value)
	{
		string[] values = value.Split(" ");
		float x = float.Parse(values[1]);
		float y = float.Parse(values[2]);
		float z = float.Parse(values[3]);
		return new Vector3(x, y, z);
	}

	Vector2 LoadVector2(string value)
	{
		string[] values = value.Split(" ");
		float x = float.Parse(values[1]);
		float y = float.Parse(values[2]);
		return new Vector2(x, y);
	}

	Triangle LoadFace(string value)
	{
		Triangle tri = new Triangle();
		string[] verts = value.Split(" ");
		string[][] values = new string[verts.Length][];
		for (int i = 0; i < verts.Length; i++)
			values[i] = verts[i].Split("/");

		tri.points.x = int.Parse(values[1][0]) - 1;
		tri.points.y = int.Parse(values[2][0]) - 1;
		tri.points.z = int.Parse(values[3][0]) - 1;

		tri.uv.x = int.Parse(values[1][1]) - 1;
		tri.uv.y = int.Parse(values[2][1]) - 1;
		tri.uv.z = int.Parse(values[3][1]) - 1;

		tri.normal = int.Parse(values[1][2]) - 1;
		return tri;
	}
	/*
	List<Triangle> Triangulate(PMesh mesh)
	{
		Vector3[] verts = mesh.verts;
		Vector3[] normals = mesh.normals;
		int[][] faces = mesh.faces;

		

		List<Triangle> list = new List<Triangle>();
		for(int i = 0; i < faces.GetLength(0); i++)
		{
			Vector3 a = verts[faces[i][1] - 1];
			Vector3 b = verts[faces[i][2] - 1];
			Vector3 c = verts[faces[i][3] - 1];
			Vector3 normal = normals[faces[i][0] - 1];
			Triangle tri = makeTri(a,b, c, normal, mesh.material);
			list.Add(tri/*transformTri(tri, mesh.position, mesh.rotationSpeed*Time.time, 1));
			int stride = faces[i].GetLength(0);
			/*for (int j = 2; j < stride; j += 2)
			{
				list.Add(makeTri(verts[faces[i][j%stride]-1], verts[faces[i][(j+1)%stride]-1], verts[faces[i][(j+2)%stride]-1], material));
			}
		}
		return list;
	}*/



	#endregion

	#region debug
	void printTri(Triangle tri, Vector3[] verts)
	{
		Debug.Log($"Point A: {verts[tri.points.x]},\nPoint B: {verts[tri.points.y]},\nPoint C: {verts[tri.points.z]}");
	}

	

	#endregion
}

#region structs

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

struct Triangle
{
	public Vector3Int points;
	public Vector3Int uv;
	public int normal;
}

struct PMesh
{
	public Vector3 position;
	public float rotationSpeed;
	public Vector3[] verts;
	public Vector3[] normals;
	public int[][] faces;
	public PMaterial material;
}

#endregion