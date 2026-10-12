using System.Collections;
using UnityEngine;

public class RobotMachiningCycle : MonoBehaviour
{
    public RobotGrabController robot;
    public Rigidbody raw;
    public Transform finished;
    public Vector3 machinePosition;
    public float machineTableHeight;
    public Transform spindle;
    public MachineOperationController machine;
    public ConveyorFeedController conveyor;
    public int CompletedCount { get; private set; }
    public string Message { get; private set; } = "点击机械臂：取原料 → 送入机床 → 加工 → 成品放入成品台";
    Vector3 sourcePosition, finishedPosition, homeGripPosition;
    Quaternion sourceRotation, finishedRotation, homeGripRotation, uprightFinishedRotation;
    Quaternion spindleRest;
    Vector3 localPalmDirection;
    bool running, moveFailed;
    float tableTop,minimumWithdrawalGap;
    bool checkingWithdrawal;
#if UNITY_EDITOR
    bool checkingReturn;
    MeshFilter[] armMeshes;
    MeshCollider[] armProbes;
    GameObject clearanceRoot;
    readonly System.Collections.Generic.HashSet<string> cycleHits=new System.Collections.Generic.HashSet<string>();
    readonly Collider[] returnContacts=new Collider[128];
    readonly System.Collections.Generic.HashSet<string> returnHits=new System.Collections.Generic.HashSet<string>();
#endif

    void Awake()
    {
        sourcePosition=conveyor?conveyor.pickupPosition:raw.transform.position;
        sourceRotation=raw.transform.rotation;
        finishedPosition=finished.position;
        finishedRotation=finished.rotation;
        RobotGrabController.ProjectionRange(finished,Vector3.zero,Vector3.up,out tableTop,out _);
        homeGripRotation=robot.grip.rotation;
        homeGripPosition=robot.grip.position;
        localPalmDirection=robot.grip.InverseTransformDirection(robot.grip.parent.position-robot.grip.position).normalized;
        uprightFinishedRotation=Quaternion.FromToRotation(Quaternion.Euler(0,-65,0)*Vector3.right,Vector3.up)*finishedRotation;
        if(spindle) spindleRest=spindle.localRotation;
        finished.gameObject.SetActive(false);
#if UNITY_EDITOR
        var meshes=new System.Collections.Generic.List<MeshFilter>(robot.GetComponentsInChildren<MeshFilter>());
        meshes.AddRange(raw.GetComponentsInChildren<MeshFilter>());
        meshes.AddRange(finished.GetComponentsInChildren<MeshFilter>(true));
        armMeshes=meshes.ToArray();
        clearanceRoot=new GameObject("Editor clearance diagnostics");
        armProbes=new MeshCollider[armMeshes.Length];
        for(int i=0;i<armMeshes.Length;i++)
        {
            var probe=new GameObject("Arm clearance probe");
            probe.transform.SetParent(clearanceRoot.transform,false);
            probe.transform.position=Vector3.one*1000;
            probe.transform.localScale=armMeshes[i].transform.lossyScale;
            armProbes[i]=probe.AddComponent<MeshCollider>();
            armProbes[i].sharedMesh=armMeshes[i].sharedMesh;
            armProbes[i].convex=true;
        }
        // Verify the narrow-phase query with a known intersecting volume so a
        // missing/invalid convex shape cannot silently look collision-free.
        var checkObject=new GameObject("Clearance query self check");
        checkObject.transform.position=Vector3.one*1010;
        var checkBox=checkObject.AddComponent<BoxCollider>();
        checkBox.size=Vector3.one*5;
        Physics.SyncTransforms();
        for(int i=0;i<armMeshes.Length;i++)if(armMeshes[i].name.StartsWith("tubular_"))
        {
            var mesh=armMeshes[i];
            bool verified=Physics.ComputePenetration(armProbes[i],mesh.transform.position,mesh.transform.rotation,checkBox,mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center),Quaternion.identity,out _,out _);
            Debug.Log("[Machining] CLEARANCE QUERY SELF CHECK: "+verified);
            break;
        }
        Destroy(checkObject);
#endif
    }

    public void StartCycle()
    {
        if(!running && !robot.IsDamaged) StartCoroutine(Cycle());
    }

    IEnumerator Pause() { while(robot.IsDamaged) yield return null; }

    IEnumerator Move(Vector3 target,float seconds)
    {
        if(moveFailed)yield break;
        yield return robot.WorkflowMove(target,seconds/1.25f);
        if(!robot.Reached(target))
        {
            moveFailed=true;
            Debug.LogWarning("[Machining] Unreachable waypoint="+target+" actual="+robot.grip.position);
        }
    }

    void Attach(Transform part)
    {
        Vector3 position=part.position;
        Quaternion rotation=part.rotation;
        var body=part.GetComponent<Rigidbody>();
        if(body){body.interpolation=RigidbodyInterpolation.None;body.isKinematic=true;}
        part.SetPositionAndRotation(position,rotation);
        part.SetParent(robot.grip,true);
        robot.SetWorkflowState(true,true);
    }

    IEnumerator Cycle()
    {
        running=true;moveFailed=false;
#if UNITY_EDITOR
        cycleHits.Clear();
#endif
        robot.SetWorkflowState(true,false);
        Vector3 entryOutward=robot.machineApproach.position-machinePosition;
        entryOutward.y=0;
        // Approach through the door with the wrist slightly above the jaws.
        Vector3 entryPalm=(entryOutward.normalized+Vector3.up*.20f).normalized;
        robot.SetGripOrientation(Quaternion.FromToRotation(homeGripRotation*localPalmDirection,entryPalm)*homeGripRotation);
        finished.SetParent(null,true);
        finished.gameObject.SetActive(false);
        raw.gameObject.SetActive(true);
        raw.isKinematic=conveyor!=null;
        raw.interpolation=conveyor?RigidbodyInterpolation.None:RigidbodyInterpolation.Interpolate;
        if(machine)machine.IsBusy=true;
        if(conveyor){Message="1/4 输送带送料中，机械臂等待…";yield return conveyor.Deliver();}
        Message=conveyor?"1/4 输送带已停，机械臂正在取料…":"1/4 正在从原料台取料…";
        yield return Move(sourcePosition+Vector3.up*.18f,.65f);
        yield return Move(raw.position,.55f);
        if(moveFailed){yield return Recover();yield break;}
        yield return robot.WorkflowFingers(false);
        Attach(raw.transform);
        yield return Move(sourcePosition+Vector3.up*.18f,.5f);
        if(machine){Message="机床开门，准备上料…";yield return machine.OpenDoor();}
        Message="2/4 正在将原料送入机床…";
        yield return Move(robot.machineApproach.position,.7f);
        yield return Move(machinePosition+Vector3.up*.04f,.7f);
        yield return Move(machinePosition,.55f);
        if(moveFailed){yield return Recover();yield break;}
        raw.transform.SetParent(null,true);
        raw.transform.SetPositionAndRotation(machinePosition,sourceRotation);
        // The machining fixture holds the blank in place during processing.
        raw.isKinematic=true;
        robot.SetWorkflowState(true,false);
        yield return robot.WorkflowFingers(true);
        yield return Move(machinePosition+Vector3.up*.04f,.4f);
        yield return Move(robot.machineApproach.position,.65f);
        if(moveFailed){yield return Recover();yield break;}
        yield return Move(robot.machineHold.position,.4f);
        if(moveFailed){yield return Recover();yield break;}
        if(machine){Message="机械臂已退出：关门、工作台移入加工…";yield return machine.Process(raw.transform);}
        Debug.Log("[Machining] LOADED: raw="+raw.transform.position);
        for(float t=0;t<3;t+=Time.deltaTime)
        {
            yield return Pause();
            Message="3/4 机床加工中："+Mathf.RoundToInt(t/3*100)+"%";
            if(spindle)spindle.Rotate(Vector3.up,720*Time.deltaTime,Space.World);
            yield return null;
        }
        yield return Pause();
        if(spindle)spindle.localRotation=spindleRest;
        raw.gameObject.SetActive(false);
        finished.SetPositionAndRotation(machine?raw.position:machinePosition,uprightFinishedRotation);
        if(machine)finished.SetParent(machine.carriage,true);
        finished.gameObject.SetActive(true);
        if(machine){Message="加工完成：工作台返回、开门…";yield return machine.ReturnTable();}
        raw.transform.SetParent(null,true);
        finished.SetParent(null,true);
        finished.SetPositionAndRotation(machinePosition,uprightFinishedRotation);
        finished.gameObject.SetActive(true);
        Debug.Log("[Machining] PROCESSED: finished="+finished.position);
        Message="4/4 正在取出阶梯轴并送往成品台…";
        yield return Move(machinePosition+Vector3.up*.04f,.65f);
        yield return Move(machinePosition,.5f);
        if(moveFailed){yield return Recover();yield break;}
        yield return robot.WorkflowFingers(false);
        Attach(finished);
        yield return Move(machinePosition+Vector3.up*.04f,.45f);
        yield return Move(robot.machineApproach.position,.7f);
        yield return Move(robot.machineHold.position,.4f);
        yield return Move(finishedPosition+Vector3.up*.25f,.85f);
        if(moveFailed){yield return Recover();yield break;}
        // Keep the shaft horizontal, but roll the wrist around its axis so the
        // palm stays above it and both jaws open sideways across the tray.
        Quaternion placement=finishedRotation*Quaternion.Inverse(finished.localRotation);
        Vector3 shaftAxis=Quaternion.Euler(0,-65,0)*Vector3.right;
        Vector3 palm=Vector3.ProjectOnPlane(placement*localPalmDirection,shaftAxis).normalized;
        float roll=Vector3.SignedAngle(palm,Vector3.up,shaftAxis);
        robot.SetGripOrientation(Quaternion.AngleAxis(roll,shaftAxis)*placement);
        yield return Move(finishedPosition+Vector3.up*.25f,.7f);
        RobotGrabController.ProjectionRange(finished,robot.grip.position,Vector3.up,out float partBottom,out _);
        finishedPosition.y=tableTop+.003f-partBottom;
        yield return Move(finishedPosition,.6f);
        if(moveFailed){yield return Recover();yield break;}
        finished.SetParent(null,true);
        robot.SetWorkflowState(true,false);
        yield return robot.WorkflowRelease(finished);
        Debug.Log("[Machining] OUTPUT: error="+Vector3.Distance(finished.position,finishedPosition)+" position="+finished.position);
        // Withdraw along the open jaw channel before rotating the empty wrist.
        minimumWithdrawalGap=ReleaseGap();checkingWithdrawal=true;
        Message="4/4 成品已放稳，夹爪正在缓慢退离…";
        yield return Move(finishedPosition+Vector3.up*.30f,1f);
        checkingWithdrawal=false;
        Debug.Log("[Machining] WITHDRAWAL CLEARANCE: "+minimumWithdrawalGap.ToString("F4")+" m");
        if(moveFailed){yield return Recover();yield break;}
        Message="4/4 机械臂正在返回待机位置…";
#if UNITY_EDITOR
        checkingReturn=true;returnHits.Clear();
#endif
        Vector3 awayFromMachine=robot.machineApproach.position-machinePosition;
        awayFromMachine.y=0;
        yield return Move(robot.grip.position+awayFromMachine.normalized*.32f,.65f);
        if(moveFailed){yield return Recover();yield break;}
        // Turn above the output station, away from the machine. Returning via
        // the door hold point while turning sweeps the forearm across the casing.
        robot.SetGripOrientation(homeGripRotation);
        yield return Move(robot.grip.position,1.1f);
        if(moveFailed){yield return Recover();yield break;}
        // Keep the tool on the outside route instead of interpolating all joint
        // angles at once, which bows the tool path back through the side shelf.
        if(machine){Message="机械臂已避开门板，机床关门…";yield return machine.Finish();}
        yield return Move(homeGripPosition,.9f);
        if(moveFailed){yield return Recover();yield break;}
#if UNITY_EDITOR
        checkingReturn=false;
        Debug.Log("[Machining] RETURN CHECK: machine contacts="+returnHits.Count);
#endif

        CompletedCount++;
        // Replenish the single supply slot; keep the finished part visible until next cycle.
        raw.transform.SetParent(null,true);
        raw.transform.SetPositionAndRotation(sourcePosition,sourceRotation);
        raw.isKinematic=false;
        raw.interpolation=RigidbodyInterpolation.Interpolate;
        raw.gameObject.SetActive(true);
        raw.linearVelocity=Vector3.zero;raw.angularVelocity=Vector3.zero;
        if(conveyor)conveyor.StageNext();
        if(machine)machine.IsBusy=false;
        running=false;robot.SetWorkflowState(false,false);
        Message="加工完成（"+CompletedCount+" 次）：阶梯轴已放入成品台。单击再加工一件";
        Debug.Log("[Machining] COMPLETE: cycles="+CompletedCount);
#if UNITY_EDITOR
        Debug.Log("[Machining] FULL CYCLE CHECK: contacts="+cycleHits.Count);
#endif
    }

    float ReleaseGap()
    {
        float gap=float.PositiveInfinity;
        for(int i=0;i<robot.fingers.Length;i++)
        {
            Vector3 side=robot.fingers[i].parent.TransformVector(robot.fingerSlideAxes[i]).normalized;
            RobotGrabController.ProjectionRange(finished,finished.position,side,out _,out float outer);
            RobotGrabController.ProjectionRange(robot.fingers[i],finished.position,side,out float inner,out _);
            gap=Mathf.Min(gap,inner-outer);
        }
        return gap;
    }

    void LateUpdate()
    {
        if(checkingWithdrawal)minimumWithdrawalGap=Mathf.Min(minimumWithdrawalGap,ReleaseGap());
#if UNITY_EDITOR
        if(running)
        {
            Physics.SyncTransforms();
            for(int meshIndex=0;meshIndex<armMeshes.Length;meshIndex++)
            {
                var mesh=armMeshes[meshIndex];
                if(!mesh.gameObject.activeInHierarchy)continue;
                if(mesh.transform.IsChildOf(raw.transform) && raw.transform.parent!=robot.grip && !(conveyor && conveyor.IsMoving))continue;
                if(mesh.transform.IsChildOf(finished) && finished.parent!=robot.grip)continue;
                Bounds bounds=mesh.sharedMesh.bounds;
                Vector3 size=Vector3.Scale(bounds.extents,mesh.transform.lossyScale);
                size=new Vector3(Mathf.Abs(size.x),Mathf.Abs(size.y),Mathf.Abs(size.z));
                int count=Physics.OverlapBoxNonAlloc(mesh.transform.TransformPoint(bounds.center),size,returnContacts,mesh.transform.rotation);
                for(int i=0;i<count;i++)
                {
                    var hit=returnContacts[i];
                    if(hit.transform.root.name!="Haas Mill - Open Loading Bay" && !(conveyor && hit.transform.IsChildOf(conveyor.transform)))continue;
                    if(!Physics.ComputePenetration(armProbes[meshIndex],mesh.transform.position,mesh.transform.rotation,hit,hit.transform.position,hit.transform.rotation,out _,out float depth))continue;
                    string key=Message+":"+mesh.name+":"+hit.name;
                    if(cycleHits.Add(key))Debug.LogWarning("[Machining] FULL CONTACT: "+key+" depth="+depth.ToString("F4")+" grip="+robot.grip.position);
                }
            }
        }
        if(checkingReturn)
        {
            for(int link=2;link<robot.joints.Length;link++)
            {
                Vector3 a=robot.joints[link].position;
                Vector3 b=link+1<robot.joints.Length?robot.joints[link+1].position:robot.grip.position;
                int count=Physics.OverlapCapsuleNonAlloc(a,b,.07f,returnContacts);
                for(int i=0;i<count;i++)
                {
                    var hit=returnContacts[i];
                    if(hit.transform.root.name!="Haas Mill - Open Loading Bay")continue;
                    string key=link+":"+hit.name;
                    if(returnHits.Add(key))Debug.LogWarning("[Machining] RETURN CLOSE CONTACT: "+key+" grip="+robot.grip.position);
                }
            }
        }
#endif
    }

    void OnDestroy()
    {
#if UNITY_EDITOR
        if(clearanceRoot)Destroy(clearanceRoot);
#endif
    }

    IEnumerator Recover()
    {
        yield return Pause();
        raw.transform.SetParent(null,true);
        raw.transform.SetPositionAndRotation(sourcePosition,sourceRotation);
        raw.gameObject.SetActive(true);
        raw.isKinematic=false;raw.interpolation=RigidbodyInterpolation.Interpolate;
        raw.linearVelocity=Vector3.zero;raw.angularVelocity=Vector3.zero;
        finished.SetParent(null,true);finished.gameObject.SetActive(false);
        if(conveyor)conveyor.StageNext();
        robot.SetGripOrientation(homeGripRotation);
        yield return robot.WorkflowFingers(true);
        yield return robot.WorkflowHome();
        if(machine)machine.IsBusy=false;
        running=false;robot.SetWorkflowState(false,false);
        Message="该动作目标暂时不可达，已复位；请单击重试";
    }
}
