using UnityEngine;
using UnityEditor;
using System;
using System.Collections;
using System.Reflection;
using System.Linq;



public class BakeAOEditor : EditorWindow
{
	enum TraceType
	{
		TraceSelf,
		TraceScene
	}

	GameObject target = null;

	int rayCount = 2048;
	float maxDistance = 1.0f;
	bool usePhysicsColliders = false;
	TraceType traceType = TraceType.TraceScene;

	string message = "";
	MessageType messageType = MessageType.None;


	delegate bool HandleUtility_IntersectRayMesh(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit raycastHit);
	static readonly HandleUtility_IntersectRayMesh IntersectRayMesh = null;



	// Static Constructor
	static BakeAOEditor()
	{            
		MethodInfo methodIntersectRayMesh = typeof(HandleUtility).GetMethod("IntersectRayMesh", BindingFlags.Static | BindingFlags.NonPublic);

		if (methodIntersectRayMesh != null)
		{
			IntersectRayMesh = delegate(Ray ray, Mesh mesh, Matrix4x4 matrix, out RaycastHit raycastHit)
			{
				object[] parameters = new object[] { ray, mesh, matrix, null };
				bool result = (bool)methodIntersectRayMesh.Invoke(null, parameters);
				raycastHit = (UnityEngine.RaycastHit)parameters[3];
				return result;
			};
		}
	}


	// Unity Editor Menu Item
	[MenuItem ("Window/BakeAO")]
	static void Init ()
	{
		// Get existing open window or if none, make a new one:
		BakeAOEditor window = (BakeAOEditor)EditorWindow.GetWindow (typeof (BakeAOEditor));
		window.ShowUtility();
	}




	void OnEnable () 
	{
		hideFlags = HideFlags.HideAndDontSave;

		if (Selection.selectionChanged != OnSelectionChanged)
			Selection.selectionChanged += OnSelectionChanged;
		
		#if (UNITY_5_0)
		title = "BakeAO";
		#else
		titleContent = new GUIContent("BakeAO", "");
		#endif

		OnSelectionChanged();
	}

	void OnDisable()
	{
		Selection.selectionChanged -= OnSelectionChanged;
	}



	void OnSelectionChanged()
	{
		target = null;

		if(Selection.objects.Length == 0) {
			message = "Select object to bake";
			messageType = MessageType.Info;
		}

		if(Selection.objects.Length > 1) {
			message = "Can't handle multiply objects";
			messageType = MessageType.Warning;
		}

		if (Selection.objects.Length == 1)
		{
			GameObject gameObject = null;
			MeshFilter meshFilter = null;

			if (Selection.objects[0] is GameObject)
			{
				gameObject = Selection.objects[0] as GameObject;
				meshFilter = gameObject.GetComponent<MeshFilter>();
			}

			if (meshFilter == null || meshFilter.sharedMesh == null) {				
				message = "Select mesh game object";
				messageType = MessageType.Warning;
			} else {				
				message = "Press 'Bake' to bake AO";
				messageType = MessageType.Info;
				target = gameObject;
			}
		}

		Repaint();
	}


	void OnGUI ()
	{
		EditorGUILayout.Space();

		EditorGUILayout.HelpBox(message, messageType);		

		rayCount = EditorGUILayout.IntField("Ray Count", rayCount);
		maxDistance = EditorGUILayout.FloatField("Max Distance", maxDistance);
		traceType = (TraceType)EditorGUILayout.EnumPopup("Trace Type", traceType);
		if (traceType == TraceType.TraceScene)
			usePhysicsColliders = EditorGUILayout.Toggle("Use Colliders (faster)", usePhysicsColliders);



		GUI.enabled = target != null;

		EditorGUILayout.BeginHorizontal ();
		GUILayout.FlexibleSpace();
		if(GUILayout.Button ("Bake", GUILayout.Width (65)))
		{
			Bake();
		}
		GUILayout.FlexibleSpace();
		EditorGUILayout.EndHorizontal ();

		GUI.enabled = true;
	}


	void Bake()
	{
		MeshFilter meshFilter = target.GetComponent<MeshFilter>();

		// Clone
		Mesh clonedMesh = Instantiate(meshFilter.sharedMesh);

		GameObject[] sceneGameObjects = UnityEngine.Object.FindObjectsOfType(typeof(GameObject)) as GameObject[];

		BakeAO(clonedMesh, meshFilter.transform.localToWorldMatrix, sceneGameObjects);

		meshFilter.sharedMesh = clonedMesh;
	}


	bool SceneRaycast(Ray ray, float maxDistance, GameObject[] sceneGameObjects, out RaycastHit raycastHit)
	{
		RaycastHit closestHit = new RaycastHit();
		closestHit.distance = Mathf.Infinity;

		foreach(GameObject gameObject in sceneGameObjects)
		{
			MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
			if (meshFilter && meshFilter.sharedMesh)
			{
				MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
				if (renderer != null)
				{
					if(!renderer.bounds.IntersectRay(ray))
						continue;
				}

				if(IntersectRayMesh(ray, meshFilter.sharedMesh, meshFilter.transform.localToWorldMatrix, out raycastHit))
				{
					if (raycastHit.distance < maxDistance && raycastHit.distance < closestHit.distance)
						closestHit = raycastHit;
				}
					
			}
		}
		raycastHit = closestHit;
		return closestHit.distance < Mathf.Infinity;
	}


	void BakeAO(Mesh mesh, Matrix4x4 localToWorldMatrix, GameObject[] sceneGameObjects)
	{
		Vector3[] vertices = mesh.vertices;
		Vector3[] normals = mesh.normals;
		Color[] colors = new Color[vertices.Length];

		RaycastHit raycastHit;
		float factor = 1.0f / rayCount;
		bool isHit;

		for (int i = 0; i < vertices.Length; i++)
		{
			Vector3 position = localToWorldMatrix.MultiplyPoint(vertices[i]);
			Vector3 normal = localToWorldMatrix.MultiplyVector(normals[i]);

			Ray ray = new Ray(position + normal * 0.001f, Vector3.zero);

			for(int j = 0; j < rayCount; j++)
			{
				ray.direction = RandomVector(normal);

				if (traceType == TraceType.TraceScene)
				{
					if (usePhysicsColliders)
						isHit = Physics.Raycast(ray, maxDistance);
					else
						isHit = SceneRaycast(ray, maxDistance, sceneGameObjects, out raycastHit);
				}
				else
				{
					isHit = IntersectRayMesh(ray, mesh, localToWorldMatrix, out raycastHit);
				}

				if(!isHit)
				{
					colors[i].r += factor;
					colors[i].g += factor;
					colors[i].b += factor;
				}
			}

			colors[i].a = 1.0f;
		}

		mesh.colors = colors;
	}

	Vector3 RandomVector(Vector3 dir)
	{
		Vector3 random = UnityEngine.Random.onUnitSphere;
		if (Vector3.Dot(random, dir) < 0.0f)
			return -random;
		return random;
	}
}

