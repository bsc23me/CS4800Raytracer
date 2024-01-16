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
	public const int TextureStride = 3;
	public const string ModelPath = "Assets/Models/";

	private RenderTexture rt;
	[SerializeField] private ComputeShader rayTracingShader;

	[SerializeField] private int TILE_SIZE = 8;
	Camera cam;

	[SerializeField] private bool sunLight = true;
	[SerializeField] private Light sun;
	[SerializeField] int sphereCount = 20;
	[SerializeField] bool rotate = true;
	[SerializeField] bool fastMath = false;

	int TEXTURE_WIDTH = 1024;
	int TEXTURE_HEIGHT = 1024;

	ComputeBuffer sphereBuffer;
	ComputeBuffer vertexBuffer;
	ComputeBuffer normalBuffer;
	ComputeBuffer UVBuffer;
	ComputeBuffer meshBuffer;
	ComputeBuffer IndicesBuffer;

	List<PMesh> meshes;
	List<Vector3> staticVertices;
	List<Vector3> staticNormals;
	List<Vector2> staticUVs;
	List<Triangle> staticTriangles;
	List<Texture2D> textures; Texture3D staticTextures;
	List<PMesh> staticIndices;

	[SerializeField] Texture2D texture;
	[SerializeField] Texture2D normalMap;

	[SerializeField] Material material;

	[SerializeField] GameObject SceneObj;

	private PMaterial blueMat = makeMat(Vector3.forward, 0f);
	private PMaterial mirrorMat = makeMat(Vector3.one, 1f);
	private PMaterial metalMat = makeMat(Vector3.one, 0.9f);
	private PMaterial goldMat = makeMat(new Vector3(1,1,0), 1f);

	[SerializeField, Range(0,3)] private int scene = 0;


	Texture2D met;

	private void OnEnable()
	{

		/*List<Sphere> spheres = new List<Sphere>();
		for (int i = 0; i < sphereCount; i++)
		{
			Vector3 position = UnityEngine.Random.insideUnitSphere * 25;
			position = new Vector3(position.x,1,position.z);
			Sphere testBall = makeSphere(position, UnityEngine.Random.Range(1,3), UnityEngine.Random.insideUnitSphere, UnityEngine.Random.value);
			spheres.Add(testBall);
		}
		sphereBuffer = new ComputeBuffer(sphereCount, SphereSize * sizeof(float));
		sphereBuffer.SetData(spheres);*/

		// Red Square for posterity
		/*PMesh pMesh = new PMesh();
		pMesh.verts = new Vector3[] { Vector3.zero, Vector3.right, new Vector3(1, 1, 0), Vector3.up };
		int[] f1 = new int[] { 0, 1, 2 };
		int[] f2 = new int[] { 2, 3, 0 };
		pMesh.faces = new int[][] { f1, f2 };*/

		staticVertices = new List<Vector3>();
		staticNormals = new List<Vector3>();
		staticUVs = new List<Vector2>();
		staticTriangles = new List<Triangle>();
		textures = new List<Texture2D>();
		
		staticIndices = new List<PMesh>() { createMesh(0, 0, 0, 0) };

		LoadScene();
		Debug.Log($"Loaded scene with {staticIndices.Count - 1} meshes, having ({staticIndices[1].faces}, {staticTriangles.Count}) total faces");
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

		/*foreach (PMesh mesh in meshes)
			if(mesh.rotationSpeed == 0)
				staticTriangles.AddRange(Triangulate(mesh));
			else
				dyn += mesh.faces.GetLength(0);*/
		if (material.GetTexture("_MetallicGlossMap"))
			met = (Texture2D) material.GetTexture("_MetallicGlossMap");
		else
		{
			met = new Texture2D(material.mainTexture.width, material.mainTexture.height);
			int[] data = new int[1048576];
			Array.Fill(data, (int)(255/* material.GetFloat("_Glossiness")*/));
			met.SetPixelData(data, 0);
			met.Apply();
		}

		byte[] pixels = new byte[TEXTURE_WIDTH * TEXTURE_HEIGHT * textures.Count * sizeof(int)];
		for (int i = 0; i < textures.Count; i++)
			textures[i].GetRawTextureData().CopyTo(pixels, TEXTURE_WIDTH * TEXTURE_HEIGHT * i);

		staticTextures = new Texture3D(TEXTURE_WIDTH, TEXTURE_HEIGHT, textures.Count, TextureFormat.RGBA32, false);
		staticTextures.SetPixelData(pixels, 0);
		staticTextures.Apply();

		vertexBuffer = new ComputeBuffer(staticVertices.Count, 3 * sizeof(float));
		normalBuffer = new ComputeBuffer(staticNormals.Count, 3 * sizeof(float));
		UVBuffer = new ComputeBuffer(staticUVs.Count, 2 * sizeof(float));
		meshBuffer = new ComputeBuffer(staticTriangles.Count, TriangleSize * sizeof(float));
		IndicesBuffer = new ComputeBuffer(staticIndices.Count, 5 * sizeof(int));

		vertexBuffer.SetData(staticVertices);
		normalBuffer.SetData(staticNormals);
		UVBuffer.SetData(staticUVs);
		meshBuffer.SetData(staticTriangles);
		IndicesBuffer.SetData(staticIndices);

	}

	private void OnDisable()
	{
		if (sphereBuffer != null)
			sphereBuffer.Release();
		if (vertexBuffer != null)
			vertexBuffer.Release();
		if (normalBuffer != null)
			normalBuffer.Release();
		if (UVBuffer != null)
			UVBuffer.Release();
		if (meshBuffer != null)
			meshBuffer.Release();
		if (IndicesBuffer != null)
			IndicesBuffer.Release();
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
	
	private void FixedUpdate()
	{
		/*List<Triangle> triangles = new List<Triangle>();
		foreach (PMesh mesh in meshes)
			if(mesh.rotationSpeed != 0)
				triangles.AddRange(Triangulate(mesh));
		triangles.AddRange(staticTriangles);
		meshBuffer.SetData(triangles);*/
		
		//transformMesh(0, Time.fixedDeltaTime * Mathf.Sin(Time.time) * Vector3.forward, Vector3.up);
		vertexBuffer.SetData(staticVertices);
		normalBuffer.SetData(staticNormals);
	}

	void Render(RenderTexture destination)
	{
		InitRenderTexture();

		rayTracingShader.SetTexture(0, "Result", rt);
		int threadGroupsX = Mathf.CeilToInt(Screen.width / TILE_SIZE);
		int threadGroupsY = Mathf.CeilToInt(Screen.height / TILE_SIZE);
		rayTracingShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
		Graphics.Blit(rt, destination);
	}
	
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
		rayTracingShader.SetBuffer(0, "Meshes", meshBuffer);
		rayTracingShader.SetTexture(0, "Materials", staticTextures);
		rayTracingShader.SetBuffer(0, "MeshIndex", IndicesBuffer);
		rayTracingShader.SetTexture(0, "Tex", material.mainTexture);
		rayTracingShader.SetTexture(0, "Norm", material.GetTexture("_BumpMap"));
		rayTracingShader.SetTexture(0, "Metal", met);

		rayTracingShader.SetInt("SAMPLES", 1/*scene == 0 ? 1 : 32*/);
		rayTracingShader.SetInt("TILE_SIZE", TILE_SIZE);
		rayTracingShader.SetBool("fastMath", fastMath);
		
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

			rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
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

	PMesh createMesh(int v, int vn, int vt, int f, int m = -1)
	{
		PMesh mesh = new PMesh();
		mesh.verts = v;
		mesh.normals = vn;
		mesh.uvs = vt;
		mesh.faces = f;
		mesh.material = m;
		return mesh;
	}

	PMesh addMesh(PMesh m1, PMesh m2)
	{
		PMesh mesh = new PMesh();
		mesh.verts = m1.verts + m2.verts;
		mesh.normals = m1.normals + m2.normals;
		mesh.uvs = m1.uvs + m2.uvs;
		mesh.faces = m1.faces + m2.faces;
		return mesh;
	}

	Vector3 rotateVector(Vector3 vec, Vector3 rotation)
	{
		float x, y, z;
		Vector3[] rotX = new Vector3[] {
			new Vector3(0, Mathf.Cos(rotation.x), -Mathf.Sin(rotation.x)),
			new Vector3(0, Mathf.Sin(rotation.x), Mathf.Cos(rotation.x)) };

		Vector3[] rotY = new Vector3[] {
			new Vector3(Mathf.Cos(rotation.y), 0, Mathf.Sin(rotation.y)) ,
			new Vector3(-Mathf.Sin(rotation.y), 0, Mathf.Cos(rotation.y))};

		Vector3[] rotZ = new Vector3[] {
			new Vector3(Mathf.Cos(rotation.z), -Mathf.Sin(rotation.z), 0),
			new Vector3(Mathf.Sin(rotation.z), Mathf.Cos(rotation.z), 0) };

		x = Vector3.Dot(vec, rotZ[0]);
		y = Vector3.Dot(vec, rotZ[1]);
		z = vec.z;
		Vector3 v = new Vector3(x, y, z);

		x = Vector3.Dot(v, rotY[0]);
		z = Vector3.Dot(v, rotY[1]);
		v = new Vector3(x, y, z);

		y = Vector3.Dot(v, rotX[0]);
		z = Vector3.Dot(v, rotX[1]);
		return new Vector3(x, y, z);
	}

	Vector3[] rotateVectors(Vector3[] vec, Vector3 rotation)
	{
		List<Vector3> rotVectors = new List<Vector3>();
		foreach(Vector3 v in vec)
			rotVectors.Add(rotateVector(v, rotation));
		return rotVectors.ToArray();
	}
	
	Vector3 transformVert(Vector3 vertex, Vector3 position, Vector3 rotation, Vector3 rotateAbout, Vector3 scale)
	{
		Vector3 v = vertex;
		v -= rotateAbout;
		v = rotateVector(v, rotation);
		v += rotateAbout;
		v.x *= scale.x;
		v.y *= scale.y;
		v.z *= scale.z;
		v += position;
		return v;
	}

	Vector3[] transformVerts(Vector3[] vertices, Vector3 position, Vector3 rotation, Vector3 rotateAbout, Vector3 scale)
	{
		List<Vector3> verts = new List<Vector3>();
		foreach (Vector3 vert in vertices)
			verts.Add(transformVert(vert, position, rotation, rotateAbout, scale));
		return verts.ToArray();
	}

	void transformMesh(int obj, Vector3 rotation, Vector3 rotateAbout)
	{
		Transform t = SceneObj.transform.GetChild(obj);
		List<Vector3> mesh = staticVertices.GetRange(staticIndices[obj].verts, staticIndices[obj + 1].verts - staticIndices[obj].verts);
		List<Vector3> normals = staticNormals.GetRange(staticIndices[obj].normals, staticIndices[obj + 1].normals - staticIndices[obj].normals);
		Vector3[] transformedMesh = transformVerts(mesh.ToArray(), Vector3.zero, rotation, t.position + rotateAbout, Vector3.one);
		Vector3[] transformedNormals = rotateVectors(normals.ToArray(), rotation);
		for (int i = 0; i < mesh.Count; i++)
			staticVertices[staticIndices[obj].verts + i] = transformedMesh[i];
		for (int i = 0; i < normals.Count; i++)
			staticNormals[staticIndices[obj].normals + i] = transformedNormals[i];
	}

	

	#endregion

	#region Mesh IO

	List<PMesh> LoadScene()
	{
		List<PMesh> meshes = new List<PMesh>();
		List<string> models = new List<string>();

		for(int i = 0; i < SceneObj.transform.childCount; i++)
		{
			Transform child = SceneObj.transform.GetChild(i);
			if (child.gameObject.activeSelf)
			{
				if (child.GetComponent<MeshRenderer>())
				{
					textures.Add((Texture2D)child.GetComponent<MeshRenderer>().material.mainTexture);
					textures.Add((Texture2D)child.GetComponent<MeshRenderer>().material.GetTexture("_BumpMap"));
					Texture2D metal = (Texture2D)child.GetComponent<MeshRenderer>().material.GetTexture("_MetallicGlossMap");
					if (!metal)
					{
						Texture2D tex = new Texture2D(TEXTURE_WIDTH, TEXTURE_HEIGHT);
						byte[] data = new byte[TEXTURE_WIDTH * TEXTURE_HEIGHT * sizeof(int)];
						Array.Fill(data, (byte)255);
						tex.SetPixelData(data, 0);
						tex.Apply();
						textures.Add(tex);
					}
					else
					{
						textures.Add(metal);
					}
				}
				LoadMesh(child.GetComponent<MeshFilter>().sharedMesh, child.position, child.eulerAngles, child.localScale, textures.Count / TextureStride - 1);
				Debug.Log($"Run {i} times, Obj w/{child.position} position");
			}
		}
/*
		Vector3 test = UnityEngine.Random.onUnitSphere;

		for (int i = 0; i < 2; i++)
		{
			LoadMesh(models[3], randMat(), Vector3.zero, test);
		}*/
		return meshes;
	}

	void LoadMesh(Mesh mesh, Vector3 position, Vector3 rotation, Vector3 rotateAbout, Vector3 scale, int material = -1)
	{
		
		staticVertices.AddRange(transformVerts(mesh.vertices, position, rotation, rotateAbout, scale));
		staticNormals.AddRange(rotateVectors(mesh.normals, rotation));
		staticUVs.AddRange(mesh.uv);
		staticTriangles.AddRange(LoadFaces(mesh.triangles));

		Vector3 v1 = mesh.vertices[0];
		Vector3 v2 = mesh.vertices[13];
		Vector3 v3 = mesh.vertices[12];
		Debug.Log($"{v1}, {v2}, {v3}; {mesh.normals[0]}, {Vector3.Cross(v2-v1,v3-v1)}, {Vector3.Cross(v3 - v2, v1 - v2)}, {Vector3.Cross(v1 - v3, v2 - v3)}");

		PMesh pMesh = createMesh(mesh.vertexCount, mesh.normals.Length, mesh.uv.Length, mesh.triangles.Length/3, material);

		if (staticIndices.Count > 0)
			staticIndices.Add(addMesh(pMesh, staticIndices[staticIndices.Count - 1]));
		else
			staticIndices.Add(pMesh);
	}
	void LoadMesh(Mesh mesh, Vector3 position, Vector3 rotation, Vector3 scale, int material = -1)
	{
		LoadMesh(mesh, position, rotation, Vector3.zero, scale, material);
	}

	void LoadMesh(Mesh mesh)
	{
		LoadMesh(mesh, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.one);
	}

	[Obsolete("LoadMesh(string,...) is deprecated. Please use LoadMesh(Mesh,...) to utilize Unity's builtin file IO.")]
	void LoadMesh(string path, PMaterial material, Vector3 position, Vector3 rotation)
	{
		StreamReader streamReader = new StreamReader(ModelPath + path);
		int verts = 0, normals = 0, uvs = 0, faces = 0;
		string data = streamReader.ReadToEnd();
		string[] lines = data.Split("\n");
		for(int i = 0; i < lines.Length; i++)
		{
			if (lines[i].StartsWith("v "))
			{
				verts++;
				Vector3 vert = transformVert(LoadVector(lines[i]), position, rotation, Vector3.zero, Vector3.one);
				//Vector3 vert = LoadVector(lines[i]);
				staticVertices.Add(vert);
			}
			else if (lines[i].StartsWith("vt"))
			{
				uvs++;
				staticUVs.Add(LoadVector2(lines[i]));
			}
			else if (lines[i].StartsWith("vn"))
			{
				normals++;
				staticNormals.Add(rotateVector(LoadVector(lines[i]), rotation));
			}
			else if (lines[i].StartsWith("f "))
			{
				faces++;
				staticTriangles.Add(LoadFace(lines[i]));
			}
		}
		if(staticIndices.Count > 0)
			staticIndices.Add(addMesh(createMesh(verts, normals, uvs, faces), staticIndices[staticIndices.Count-1]));
		else
			staticIndices.Add(createMesh(verts,normals,uvs, faces));
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

	Triangle LoadFace(int[] verts)
	{
		Triangle tri = new Triangle();
		tri.points.x = verts[0];
		tri.points.y = verts[1];
		tri.points.z = verts[2];

		tri.uv.x = verts[0];
		tri.uv.y = verts[1];
		tri.uv.z = verts[2];

		tri.normal = verts[0];
		// TODO: Add Phong shading
		return tri;
	}

	Triangle[] LoadFaces(int[] verts)
	{
		List<Triangle> tris = new List<Triangle>();
		for(int i = 0; i < verts.Length; i+=3)
			tris.Add(LoadFace(new int[] { verts[i], verts[i + 1], verts[i+2] }));

		return tris.ToArray();
	}

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
	/*public Vector3 position;
	public float rotationSpeed;
	public Vector3[] verts;
	public Vector3[] normals;
	public int[][] faces;
	public PMaterial material;*/

	public int verts;
	public int normals;
	public int uvs;
	public int faces;
	public int material;
	
}

#endregion