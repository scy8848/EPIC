using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class RobotGrabSetup
{
    const string ScenePath = "Assets/Scenes/RobotGrab.unity";
    static RobotGrabSetup() { EditorApplication.delayCall += FirstSetup; }
    static void FirstSetup()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !File.Exists(ScenePath)) Build();
    }
    [MenuItem("Tools/EPIC/Create Robot Grab Demo")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play mode before creating the demo."); return; }
        if (SceneManager.GetActiveScene().isDirty) { Debug.LogWarning("Save current scene before creating grab demo."); return; }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/ABB IRB Robotic Arm.fbx");
        if (!model) { Debug.LogError("Missing robot FBX."); return; }
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var robot = UnityEngine.Object.Instantiate(model);
        robot.name = "Robot Arm (Click to Grab)";
        robot.transform.rotation = Quaternion.Euler(90, 0, 0);
        Bounds bounds = BoundsOf(robot);
        robot.transform.localScale *= 2.4f / bounds.size.y;
        bounds = BoundsOf(robot);
        robot.transform.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        var all = robot.GetComponentsInChildren<Transform>();
        Transform Find(string prefix) => all.First(t => t.name.StartsWith(prefix, StringComparison.Ordinal));
        var fingers = all.Where(t => t.name.StartsWith("SCHUNK-xxx EGH GBA", StringComparison.Ordinal)).ToArray();
        var pads = all.Where(t => t.name.StartsWith("SCHUNK-1352153", StringComparison.Ordinal)).ToArray();
        Vector3 contact = (pads[0].GetComponent<Renderer>().bounds.center + pads[1].GetComponent<Renderer>().bounds.center) * .5f;
        Vector3 jawAxis = (pads[0].GetComponent<Renderer>().bounds.center - pads[1].GetComponent<Renderer>().bounds.center).normalized;
        const float radius = .055f;
        var slides = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            Vector3 outward = i == 0 ? jawAxis : -jawAxis;
            var pad = fingers[i].GetComponentsInChildren<MeshFilter>().First(f => f.name.StartsWith("SCHUNK-1352153"));
            float innerFace = pad.sharedMesh.vertices.Min(v => Vector3.Dot(pad.transform.TransformPoint(v) - contact, outward));
            fingers[i].position += outward * (radius - innerFace);
            slides[i] = fingers[i].parent.InverseTransformVector(outward);
        }
        var grip = new GameObject("GripPoint").transform;
        grip.SetParent(Find("xxxxxx EGH"), false);
        grip.position = contact;
        Vector3 reach = contact - Find("3disk_").position;
        reach.y = 0;
        grip.rotation = Quaternion.LookRotation(reach.normalized, Vector3.up);
        foreach (var r in robot.GetComponentsInChildren<MeshRenderer>())
        {
            var collider = r.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            r.sharedMaterial = Mat(r.name.StartsWith("SCHUNK") ? new Color(.15f,.18f,.23f) : new Color(.95f,.36f,.08f));
        }
        var controller = robot.AddComponent<RobotGrabController>();
        controller.joints = new[] {Find("swing.step"), Find("lower arm"), Find("housing_900"), Find("tubular_"), Find("tilt_"), Find("3disk_")};
        Vector3 radial = Find("3disk_").position - Find("swing.step").position;
        radial.y = 0;
        Vector3 hinge = Vector3.Cross(radial.normalized, Vector3.up).normalized;
        controller.axes = new[] {
            controller.joints[0].InverseTransformDirection(Vector3.up),
            controller.joints[1].InverseTransformDirection(hinge),
            controller.joints[2].InverseTransformDirection(hinge),
            controller.joints[3].InverseTransformDirection((Find("tilt_").position-Find("tubular_").position).normalized),
            controller.joints[4].InverseTransformDirection(hinge),
            controller.joints[5].InverseTransformDirection((Find("3disk_").position-Find("tilt_").position).normalized)};
        controller.grip = grip;
        controller.fingers = fingers;
        controller.fingerSlideAxes = slides;
        // Start with a reachable target near the existing gripper pose.
        Vector3 objectPosition = grip.position - Vector3.up * .22f;
        var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = "Temporary Cylinder";
        cylinder.transform.localScale = new Vector3(radius * 2,.09f,radius * 2);
        cylinder.transform.position = objectPosition;
        cylinder.GetComponent<Renderer>().sharedMaterial = Mat(new Color(.08f,.72f,.84f));
        var body = cylinder.AddComponent<Rigidbody>();
        body.mass = .3f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        controller.item = body;
        var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pedestal.name = "Pickup Table";
        float tableTop = objectPosition.y - .09f;
        pedestal.transform.localScale = new Vector3(.65f,Mathf.Max(.05f,tableTop),.65f);
        pedestal.transform.position = new Vector3(objectPosition.x,tableTop/2,objectPosition.z);
        pedestal.GetComponent<Renderer>().sharedMaterial = Mat(new Color(.21f,.26f,.34f));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Workshop Floor";
        floor.transform.localScale = Vector3.one;
        floor.GetComponent<Renderer>().sharedMaterial = Mat(new Color(.36f,.40f,.46f));
        var cam = Camera.main;
        bounds = BoundsOf(robot);
        Vector3 focus = bounds.center;
        cam.transform.position = focus + new Vector3(3.4f,2.2f,3.4f);
        cam.transform.LookAt(focus);
        cam.fieldOfView = 43;
        cam.nearClipPlane = .05f;
        controller.viewCamera = cam;
        cam.gameObject.AddComponent<RobotOrbitCamera>().focus = focus;
        RenderSettings.ambientLight = Color.gray;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
        AssetDatabase.SaveAssets();
        File.WriteAllText("RobotGrabSetup.txt", "Grip="+grip.position+" Item="+objectPosition+"\n"+string.Join("\n",all.Select(t=>t.name+" pos="+t.position+" rot="+t.eulerAngles)));
        Debug.Log("[RobotGrab] Scene created and saved: " + ScenePath);
    }
    [MenuItem("Tools/EPIC/Create Machine Grab Demo")]
    public static void BuildMachine()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Haas Mill.fbx");
        if (!model || EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty) return;
        Build();
        var robot = UnityEngine.Object.FindFirstObjectByType<RobotGrabController>();
        UnityEngine.Object.DestroyImmediate(GameObject.Find("Pickup Table"));
        var machine = UnityEngine.Object.Instantiate(model);
        machine.name = "Haas Mill - Open Loading Bay";
        machine.transform.rotation = Quaternion.Euler(0, -65, 0) * model.transform.rotation;
        machine.transform.localScale *= 1.4f;
        Quaternion heading = Quaternion.Euler(0, -65, 0);
        machine.transform.position -= Vector3.up * BoundsOf(machine).min.y;
        var tableRenderer = machine.GetComponentsInChildren<Renderer>().First(r => r.name == "37-1466-DEF");
        Vector3 table = new Vector3(.95f, tableRenderer.bounds.max.y, -.44f);
        machine.transform.position += table - new Vector3(tableRenderer.bounds.center.x, tableRenderer.bounds.max.y, tableRenderer.bounds.center.z);
        var door = machine.GetComponentsInChildren<Transform>().First(t => t.name == "37-4532-DEF-DOOR");
        door.position += heading * Vector3.right * .78f;
        foreach (var r in machine.GetComponentsInChildren<MeshRenderer>())
        {
            var c = r.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            if (r.name.Contains("DOOR") || r.name == "37-4532-DEF") r.sharedMaterial = Mat(new Color(.74f,.77f,.80f));
            else if (r.name.StartsWith("37-146") || r.name.StartsWith("20T") || r.name.StartsWith("37-148")) r.sharedMaterial = Mat(new Color(.32f,.36f,.41f));
            else r.sharedMaterial = Mat(new Color(.60f,.63f,.67f));
        }
        robot.item.transform.position = table + Vector3.up * .09f;
        Vector3 outward = heading * Vector3.forward;
        var approach = new GameObject("Machine Door Approach").transform;
        approach.position = robot.item.transform.position + outward * .73f + Vector3.up * .12f;
        var hold = new GameObject("Outside Machine Hold Point").transform;
        hold.position = approach.position + outward * .10f + Vector3.up * .15f;
        robot.machineApproach = approach;
        robot.machineHold = hold;
        robot.ConfigurePose(hold.position);
        Bounds b = BoundsOf(machine);
        b.Encapsulate(BoundsOf(robot.gameObject));
        var cam = Camera.main;
        var orbit = cam.GetComponent<RobotOrbitCamera>();
        orbit.focus = b.center + Vector3.down * .10f;
        cam.transform.position = orbit.focus + new Vector3(-3.6f, 2.6f, 4.5f);
        cam.transform.LookAt(orbit.focus);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/Scenes/RobotMachineGrab.unity");
        AssetDatabase.SaveAssets();
        File.WriteAllText("MachineSetup.txt", "Table="+table+" item="+robot.item.transform.position+" approach="+approach.position+" home="+robot.grip.position+"\n"+string.Join("\n",machine.GetComponentsInChildren<Renderer>().Select(r=>r.name+" center="+r.bounds.center+" size="+r.bounds.size)));
        Debug.Log("[RobotGrab] Machine scene saved.");
    }

    [MenuItem("Tools/EPIC/Install Raw And Finished Workpieces")]
    public static void InstallWorkpieces()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty) { Debug.LogWarning("Save scene and exit Play mode first."); return; }
        var robot = UnityEngine.Object.FindAnyObjectByType<RobotGrabController>();
        if (!robot || !robot.machineApproach) { Debug.LogError("Open RobotMachineGrab scene first."); return; }
        if(robot.machiningCycle){Debug.LogWarning("Recreate the machine demo before reinstalling workpieces in a configured machining scene.");return;}
        var rawModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/raw.fbx");
        var completeModel = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/complete.fbx");
        if (!rawModel || !completeModel) { Debug.LogError("Missing raw or complete FBX."); return; }
        var old = robot.item;
        float tableTop = BoundsOf(old.gameObject).min.y;
        Vector3 oldPosition = old.transform.position;
        Vector3 jawAxis = robot.fingers[0].parent.TransformVector(robot.fingerSlideAxes[0]).normalized;
        var raw = CenteredModel(rawModel, "Raw Material - Grabbable", Vector3.up, new Color(.08f,.72f,.84f));
        Bounds rawBounds = BoundsOf(raw);
        raw.transform.position = new Vector3(oldPosition.x, tableTop + rawBounds.extents.y, oldPosition.z);
        // Calibrate the closed jaw faces against the actual imported mesh.
        float radius = raw.GetComponentsInChildren<MeshFilter>().Max(f=>f.sharedMesh.vertices.Max(v=>Mathf.Abs(Vector3.Dot(f.transform.TransformPoint(v)-raw.transform.position,jawAxis))));
        for (int i=0;i<robot.fingers.Length;i++)
        {
            Vector3 outward = robot.fingers[i].parent.TransformVector(robot.fingerSlideAxes[i]).normalized;
            var pad = robot.fingers[i].GetComponentsInChildren<MeshFilter>().First(f=>f.name.StartsWith("SCHUNK-1352153"));
            float innerFace = pad.sharedMesh.vertices.Min(v=>Vector3.Dot(pad.transform.TransformPoint(v)-robot.grip.position,outward));
            robot.fingers[i].position += outward * (radius-innerFace);
        }
        foreach (var f in raw.GetComponentsInChildren<MeshFilter>())
        {
            var collider = f.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = f.sharedMesh;
            collider.convex = true;
        }
        var body = raw.AddComponent<Rigidbody>();
        body.mass = .3f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        robot.item = body;
        robot.itemDisplayName = "原料";
        Vector3 shift = raw.transform.position-oldPosition;
        robot.machineApproach.position += shift;
        robot.machineHold.position += shift;
        robot.ConfigurePose(robot.machineHold.position);
        UnityEngine.Object.DestroyImmediate(old.gameObject);
        var existingTable = GameObject.Find("Finished Parts Table");
        if (existingTable) UnityEngine.Object.DestroyImmediate(existingTable);
        var existingPart = GameObject.Find("Finished Stepped Shaft");
        if (existingPart) UnityEngine.Object.DestroyImmediate(existingPart);
        Quaternion heading = Quaternion.Euler(0,-65,0);
        Vector3 tablePosition = new Vector3(oldPosition.x,0,oldPosition.z) + heading*new Vector3(1.35f,0,.35f);
        var finishedTable = new GameObject("Finished Parts Table");
        var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
        top.name = "Finished Table Top";
        top.transform.SetParent(finishedTable.transform,false);
        top.transform.SetPositionAndRotation(tablePosition+Vector3.up*.95f,heading);
        top.transform.localScale = new Vector3(.75f,.08f,.55f);
        top.GetComponent<Renderer>().sharedMaterial = Mat(new Color(.19f,.27f,.34f));
        foreach (float x in new[]{-.29f,.29f}) foreach(float z in new[]{-.20f,.20f})
        {
            var leg=GameObject.CreatePrimitive(PrimitiveType.Cube);
            leg.name="Table Leg";
            leg.transform.SetParent(finishedTable.transform,false);
            leg.transform.SetPositionAndRotation(tablePosition+heading*new Vector3(x,.455f,z),heading);
            leg.transform.localScale=new Vector3(.05f,.91f,.05f);
            leg.GetComponent<Renderer>().sharedMaterial=Mat(new Color(.30f,.34f,.38f));
        }
        var complete = CenteredModel(completeModel,"Finished Stepped Shaft",heading*Vector3.right,new Color(.78f,.81f,.85f));
        Bounds completeBounds=BoundsOf(complete);
        complete.transform.position=tablePosition+Vector3.up*(.99f+completeBounds.extents.y);
        foreach(var f in complete.GetComponentsInChildren<MeshFilter>())
        {
            var collider=f.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh=f.sharedMesh;
        }
        var marker = new GameObject("成品区 - Finished Parts").AddComponent<TextMesh>();
        marker.transform.SetParent(finishedTable.transform,false);
        marker.transform.position=tablePosition+heading*new Vector3(0,.85f,.29f);
        marker.transform.rotation=heading*Quaternion.Euler(0,180,0);
        marker.text="";
        marker.anchor=TextAnchor.MiddleCenter;
        marker.alignment=TextAlignment.Center;
        marker.fontSize=48;
        marker.characterSize=.025f;
        marker.color=Color.white;
        var cam=Camera.main;
        Bounds sceneBounds=BoundsOf(GameObject.Find("Haas Mill - Open Loading Bay"));
        sceneBounds.Encapsulate(BoundsOf(robot.gameObject));
        sceneBounds.Encapsulate(BoundsOf(finishedTable));
        cam.GetComponent<RobotOrbitCamera>().focus=sceneBounds.center;
        cam.transform.position=sceneBounds.center+new Vector3(-3.8f,2.5f,5f);
        cam.transform.LookAt(sceneBounds.center);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[RobotGrab] WORKPIECES INSTALLED: raw size="+rawBounds.size+" closed jaw radius="+radius+" raw position="+raw.transform.position+" finished size="+completeBounds.size+" finished position="+complete.transform.position);
    }

    [MenuItem("Tools/EPIC/Enable Complete Machining Flow")]
    public static void EnableMachiningFlow()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)return;
        var robot=UnityEngine.Object.FindAnyObjectByType<RobotGrabController>();
        var finished=GameObject.Find("Finished Stepped Shaft");
        var outputTable=GameObject.Find("Finished Parts Table");
        if(!robot || !finished || !outputTable){Debug.LogError("Install raw and finished workpieces first.");return;}
        var cycle=robot.GetComponent<RobotMachiningCycle>();
        if(!cycle)cycle=robot.gameObject.AddComponent<RobotMachiningCycle>();
        else if(cycle.robot){Debug.Log("[Machining] Flow already configured.");return;}
        cycle.robot=robot;cycle.raw=robot.item;cycle.finished=finished.transform;
        cycle.machinePosition=robot.item.transform.position;
        cycle.machineTableHeight=BoundsOf(robot.item.gameObject).min.y;
        cycle.spindle=GameObject.Find("Haas Mill - Open Loading Bay").GetComponentsInChildren<Transform>().First(t=>t.name.StartsWith("20T Spindle"));
        robot.machiningCycle=cycle;
        Vector3 outward=(robot.machineApproach.position-cycle.machinePosition);outward.y=0;outward.Normalize();
        Vector3 source=robot.machineHold.position+outward*.15f-Vector3.up*.40f;
        robot.item.transform.position=source;
        var inputTable=GameObject.CreatePrimitive(PrimitiveType.Cube);
        inputTable.name="Raw Material Supply Table";
        float top=source.y-BoundsOf(robot.item.gameObject).extents.y;
        inputTable.transform.position=new Vector3(source.x,top/2,source.z);
        inputTable.transform.localScale=new Vector3(.36f,top,.36f);
        inputTable.GetComponent<Renderer>().sharedMaterial=Mat(new Color(.15f,.31f,.36f));
        // Both material stations stay within the existing fixed-base robot's reach.
        Vector3 outputCenter=new Vector3(.45f,.99f,.75f);
        Vector3 shift=outputCenter-(finished.transform.position-Vector3.up*BoundsOf(finished).extents.y);
        outputTable.transform.position+=shift;
        finished.transform.position+=shift;
        AdjustLoadingClearance();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Machining] CONFIGURED: source="+source+" machine="+cycle.machinePosition+" output="+finished.transform.position);
    }

    [MenuItem("Tools/EPIC/Clear Finished Table Label")]
    public static void ClearFinishedTableLabel()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var table=GameObject.Find("Finished Parts Table");
        if(!table)return;
        foreach(var label in table.GetComponentsInChildren<TextMesh>())label.text="";
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[Machining] Finished table label cleared and scene saved.");
    }

    [MenuItem("Tools/EPIC/Adjust Loading Clearance")]
    public static void AdjustLoadingClearance()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var machine=GameObject.Find("Haas Mill - Open Loading Bay");
        if(!machine || GameObject.Find("Robot Loading Clearance Layout"))return;
        Vector3 lateral=Quaternion.Euler(0,-65,0)*Vector3.right;
        foreach(var part in machine.GetComponentsInChildren<Transform>())
        {
            if(part.name=="37-4532-DEF-DOOR")part.position+=lateral*.45f;
            if(part.name=="CONV-SHELF-VF-SM" || part.name=="CONV-HOLDER-VF-XS")part.position+=lateral*.85f;
        }
        Vector3 shift=new Vector3(-.18f,0,.16f);
        GameObject.Find("Finished Parts Table").transform.position+=shift;
        GameObject.Find("Finished Stepped Shaft").transform.position+=shift;
        new GameObject("Robot Loading Clearance Layout").transform.SetParent(machine.transform,false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[Machining] Loading clearance layout saved.");
    }

    static GameObject CenteredModel(GameObject model,string name,Vector3 longAxis,Color color)
    {
        var root=new GameObject(name);
        var visual=UnityEngine.Object.Instantiate(model,root.transform);
        Bounds bounds=BoundsOf(visual);
        Vector3 sourceAxis=bounds.size.x>bounds.size.y && bounds.size.x>bounds.size.z ? Vector3.right : bounds.size.y>bounds.size.z ? Vector3.up : Vector3.forward;
        visual.transform.rotation=Quaternion.FromToRotation(sourceAxis,longAxis)*visual.transform.rotation;
        visual.transform.position-=BoundsOf(visual).center;
        foreach(var r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial=Mat(color);
        return root;
    }

    static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }
    static Material Mat(Color color)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.color = color;
        string path = "Assets/Materials/Grab_"+ColorUtility.ToHtmlStringRGB(color)+".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) { UnityEngine.Object.DestroyImmediate(m); return existing; }
        AssetDatabase.CreateAsset(m,path);
        return m;
    }
}
