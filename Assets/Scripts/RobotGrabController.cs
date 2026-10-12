using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class RobotGrabController : MonoBehaviour
{
    public Transform[] joints;
    public Vector3[] axes;
    public Transform grip;
    public Transform[] fingers;
    public Vector3[] fingerSlideAxes;
    public Rigidbody item;
    public string itemDisplayName = "圆柱体";
    public Camera viewCamera;
    public Transform machineApproach;
    public Transform machineHold;
    public RobotMachiningCycle machiningCycle;
    public bool IsHolding { get; private set; }
    public bool IsBusy { get; private set; }
    public bool IsDamaged { get; private set; }
    const float DoubleClickWindow = .32f;
    const float MotionSpeedMultiplier = 1.3f;
    bool clickPending;
    bool pendingGrab;
    float firstClickTime;
    Renderer[] robotRenderers;
    MaterialPropertyBlock[] originalAppearance;
    public string Status { get; private set; } = "Click the robot to pick up the cylinder";
    Quaternion[] home;
    Vector3[] fingerHome;
    Vector3[] fingerOpen;
    Vector3 restPosition;
    Quaternion restRotation;
    Font uiFont;
    Quaternion gripOrientation;
    readonly float[,] jacobian = new float[6, 6];
    readonly float[,] system = new float[6, 7];

    void Awake()
    {
        home = new Quaternion[joints.Length];
        for (int i = 0; i < joints.Length; i++) home[i] = joints[i].localRotation;
        fingerHome = new Vector3[fingers.Length];
        fingerOpen = new Vector3[fingers.Length];
        for (int i = 0; i < fingers.Length; i++)
        {
            fingerHome[i] = fingers[i].localPosition;
            fingerOpen[i] = fingerHome[i] + fingerSlideAxes[i] * .035f;
        }
        restPosition = item.position;
        restRotation = item.rotation;
        gripOrientation = grip.rotation;
        uiFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 22);
        foreach (var collider in GetComponentsInChildren<Collider>())
            foreach (var itemCollider in item.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(collider, itemCollider);
        SetFingers(1);
        robotRenderers = GetComponentsInChildren<Renderer>();
        originalAppearance = new MaterialPropertyBlock[robotRenderers.Length];
        for (int i = 0; i < robotRenderers.Length; i++)
        {
            originalAppearance[i] = new MaterialPropertyBlock();
            robotRenderers[i].GetPropertyBlock(originalAppearance[i]);
        }
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && !mouse.rightButton.isPressed && !mouse.middleButton.isPressed)
        {
            Ray ray = viewCamera.ScreenPointToRay(mouse.position.ReadValue());
            bool clickedRobot = !RobotOrbitCamera.IsPointerOverControls(mouse.position.ReadValue())
                && Physics.Raycast(ray, out RaycastHit hit) && hit.transform.IsChildOf(transform);
            if (clickedRobot)
            {
                if (clickPending && Time.unscaledTime - firstClickTime <= DoubleClickWindow)
                {
                    clickPending = false;
                    ToggleDamage();
                }
                else
                {
                    clickPending = true;
                    pendingGrab = !IsBusy && !IsDamaged;
                    firstClickTime = Time.unscaledTime;
                }
            }
            else clickPending = false;
        }
        // Delay single-click action so the first half of a double-click cannot grab.
        if (clickPending && Time.unscaledTime - firstClickTime > DoubleClickWindow)
        {
            clickPending = false;
            if (pendingGrab) Toggle();
        }
    }

    public void Toggle()
    {
        if (machiningCycle) { if (!IsBusy && !IsDamaged) machiningCycle.StartCycle(); return; }
        if (!IsBusy && !IsDamaged) StartCoroutine(IsHolding ? Place() : Pick());
    }

    public void SetWorkflowState(bool busy, bool holding) { IsBusy = busy; IsHolding = holding; }
    public void SetGripOrientation(Quaternion orientation) { gripOrientation = orientation; }
    public bool Reached(Vector3 target) => Vector3.Distance(grip.position,target)<.006f && Quaternion.Angle(grip.rotation,gripOrientation)<2f;
    public IEnumerator WorkflowMove(Vector3 target,float duration) { yield return MoveGrip(target,duration); }
    public IEnumerator WorkflowFingers(bool open) { yield return Fingers(open); }
    // Include the finger supports, not only the small contact pads, when releasing.
    public IEnumerator WorkflowRelease(Transform part)
    {
        yield return Fingers(true);
        Vector3[] start=new Vector3[fingers.Length],target=new Vector3[fingers.Length];
        for(int i=0;i<fingers.Length;i++)
        {
            Vector3 outward=fingers[i].parent.TransformVector(fingerSlideAxes[i]).normalized;
            ProjectionRange(part,part.position,outward,out _,out float partOuter);
            ProjectionRange(fingers[i],part.position,outward,out float fingerInner,out _);
            start[i]=fingers[i].localPosition;
            target[i]=start[i]+fingers[i].parent.InverseTransformVector(outward*Mathf.Max(0,partOuter+.020f-fingerInner));
        }
        const float releaseDuration=.28f/MotionSpeedMultiplier;
        for(float t=0;t<releaseDuration;t+=Time.deltaTime)
        {
            yield return WaitForRepair();
            for(int i=0;i<fingers.Length;i++)fingers[i].localPosition=Vector3.Lerp(start[i],target[i],Mathf.SmoothStep(0,1,t/releaseDuration));
            yield return null;
        }
        for(int i=0;i<fingers.Length;i++)fingers[i].localPosition=target[i];
    }

    public static void ProjectionRange(Transform root,Vector3 origin,Vector3 direction,out float min,out float max)
    {
        min=float.PositiveInfinity;max=float.NegativeInfinity;
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            Bounds bounds=filter.sharedMesh.bounds;
            for(int corner=0;corner<8;corner++)
            {
                Vector3 local=bounds.center+Vector3.Scale(bounds.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1));
                float projection=Vector3.Dot(filter.transform.TransformPoint(local)-origin,direction);
                min=Mathf.Min(min,projection);max=Mathf.Max(max,projection);
            }
        }
    }
    public IEnumerator WorkflowHome() { yield return ReturnHome(); }

    public void ToggleDamage()
    {
        IsDamaged = !IsDamaged;
        for (int i = 0; i < robotRenderers.Length; i++)
        {
            if (!IsDamaged) robotRenderers[i].SetPropertyBlock(originalAppearance[i]);
            else
            {
                var warning = new MaterialPropertyBlock();
                robotRenderers[i].GetPropertyBlock(warning);
                warning.SetColor("_BaseColor", new Color(.85f, .045f, .025f));
                warning.SetColor("_Color", new Color(.85f, .045f, .025f));
                robotRenderers[i].SetPropertyBlock(warning);
            }
        }
        Debug.Log("[RobotGrab] " + (IsDamaged ? "DAMAGED" : "REPAIRED") + ": busy=" + IsBusy + " holding=" + IsHolding);
    }

    IEnumerator WaitForRepair()
    {
        while (IsDamaged) yield return null;
    }

    IEnumerator Pick()
    {
        IsBusy = true;
        Status = "Reaching for cylinder...";
        Vector3 target = item.position;
        if (machineApproach) yield return MoveGrip(machineApproach.position, .65f);
        yield return MoveGrip(target + Vector3.up * (machineApproach ? .12f : .25f), .65f);
        yield return MoveGrip(target, .55f);
        if (Vector3.Distance(grip.position, target) > .006f || Quaternion.Angle(grip.rotation, gripOrientation) > 2f)
        {
            Status = "Target out of reach. Click to retry.";
            yield return ReturnHome();
            IsBusy = false;
            yield break;
        }
        yield return Fingers(false);
        Vector3 attachmentPosition = item.transform.position;
        Quaternion attachmentRotation = item.transform.rotation;
        item.interpolation = RigidbodyInterpolation.None;
        item.isKinematic = true;
        item.transform.SetPositionAndRotation(attachmentPosition, attachmentRotation);
        item.transform.SetParent(grip, true);
        IsHolding = true;
        if (machineApproach)
        {
            yield return MoveGrip(target + Vector3.up * .12f, .45f);
            yield return MoveGrip(machineApproach.position, .7f);
            yield return MoveGrip(machineHold.position, .5f);
        }
        else yield return MoveGrip(target + Vector3.up * .5f, .7f);
        Status = "Holding cylinder - click the robot to place it down";
        Debug.Log("[RobotGrab] PICKED: contact error="+Vector3.Distance(grip.position,item.transform.position)+" grip="+grip.position+" item="+item.transform.position+" local="+item.transform.localPosition);
        IsBusy = false;
    }

    IEnumerator Place()
    {
        IsBusy = true;
        Status = "Placing cylinder...";
        if (machineApproach)
        {
            yield return MoveGrip(machineApproach.position, .6f);
            yield return MoveGrip(restPosition + Vector3.up * .12f, .7f);
        }
        yield return MoveGrip(restPosition, .8f);
        if (Vector3.Distance(grip.position, restPosition) > .006f || Quaternion.Angle(grip.rotation, gripOrientation) > 2f)
        {
            Status = "Cannot reach placement point. Click to retry.";
            IsBusy = false;
            yield break;
        }
        item.transform.SetParent(null, true);
        item.isKinematic = false;
        item.linearVelocity = Vector3.zero;
        item.angularVelocity = Vector3.zero;
        item.interpolation = RigidbodyInterpolation.Interpolate;
        IsHolding = false;
        yield return Fingers(true);
        yield return MoveGrip(restPosition + Vector3.up * (machineApproach ? .12f : .25f), .5f);
        if (machineApproach) yield return MoveGrip(machineApproach.position, .7f);
        yield return ReturnHome();
        Status = "Cylinder placed - click the robot to pick it up again";
        Debug.Log("[RobotGrab] PLACED: cylinder released, physics restored");
        IsBusy = false;
    }

    IEnumerator MoveGrip(Vector3 target, float duration)
    {
        yield return WaitForRepair();
        Vector3 start = grip.position;
        Quaternion startOrientation=grip.rotation;
        Quaternion targetOrientation=gripOrientation;
        // Interpolate the orientation target too: otherwise IK turns the wrist
        // immediately while only the position follows the timed trajectory.
        duration=Mathf.Max(duration,Quaternion.Angle(startOrientation,targetOrientation)/75f);
        duration/=MotionSpeedMultiplier;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.deltaTime)
        {
            yield return WaitForRepair();
            float phase=Mathf.SmoothStep(0,1,elapsed/duration);
            gripOrientation=Quaternion.Slerp(startOrientation,targetOrientation,phase);
            Solve(Vector3.Lerp(start,target,phase));
            yield return null;
        }
        gripOrientation=targetOrientation;
        for (int i = 0; i < 60 && (Vector3.Distance(grip.position, target) > .001f || Quaternion.Angle(grip.rotation, gripOrientation) > .3f); i++)
        {
            yield return WaitForRepair();
            Solve(target);
            yield return null;
        }
        yield return WaitForRepair();
    }

    public void ConfigurePose(Vector3 target)
    {
        gripOrientation = grip.rotation;
        for (int i = 0; i < 150; i++) Solve(target);
        Debug.Log("[RobotGrab] Initial pose error=" + Vector3.Distance(grip.position, target));
    }

    void Solve(Vector3 target)
    {
        // Damped least squares solves position AND orientation; the cylinder stays
        // upright and the contact frame no longer rolls away from the item.
        const float orientationWeight = .35f;
        for (int pass = 0; pass < 10; pass++)
        {
            Vector3 errorPosition = target - grip.position;
            Quaternion errorRotation = gripOrientation * Quaternion.Inverse(grip.rotation);
            errorRotation.ToAngleAxis(out float angle, out Vector3 direction);
            if (angle > 180) angle -= 360;
            Vector3 errorOrientation = Mathf.Abs(angle) < .001f ? Vector3.zero : direction * angle * Mathf.Deg2Rad * orientationWeight;
            for (int i = 0; i < 6; i++)
            {
                Vector3 axis = joints[i].TransformDirection(axes[i]).normalized;
                Vector3 positionDerivative = Vector3.Cross(axis, grip.position - joints[i].position);
                for (int row = 0; row < 3; row++)
                {
                    jacobian[row, i] = positionDerivative[row];
                    jacobian[row + 3, i] = axis[row] * orientationWeight;
                }
            }
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 6; col++)
                {
                    float value = row == col ? .001f : 0;
                    for (int k = 0; k < 6; k++) value += jacobian[row, k] * jacobian[col, k];
                    system[row, col] = value;
                }
                system[row, 6] = row < 3 ? errorPosition[row] : errorOrientation[row - 3];
            }
            // Partial-pivot Gaussian elimination of (J J^T + damping I).
            for (int col = 0; col < 6; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < 6; row++) if (Mathf.Abs(system[row, col]) > Mathf.Abs(system[pivot, col])) pivot = row;
                for (int k = col; k <= 6; k++) { float v = system[col, k]; system[col, k] = system[pivot, k]; system[pivot, k] = v; }
                float divisor = system[col, col];
                for (int k = col; k <= 6; k++) system[col, k] /= divisor;
                for (int row = 0; row < 6; row++)
                {
                    if (row == col) continue;
                    float multiplier = system[row, col];
                    for (int k = col; k <= 6; k++) system[row, k] -= multiplier * system[col, k];
                }
            }
            for (int i = 0; i < 6; i++)
            {
                float increment = 0;
                for (int row = 0; row < 6; row++) increment += jacobian[row, i] * system[row, 6];
                joints[i].Rotate(axes[i], Mathf.Clamp(increment * Mathf.Rad2Deg, -4, 4), Space.Self);
            }
        }
    }

    IEnumerator ReturnHome()
    {
        Quaternion[] start = new Quaternion[joints.Length];
        for (int i = 0; i < joints.Length; i++) start[i] = joints[i].localRotation;
        float duration=1f;
        for(int i=0;i<joints.Length;i++)duration=Mathf.Max(duration,Quaternion.Angle(start[i],home[i])/60f);
        duration/=MotionSpeedMultiplier;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            yield return WaitForRepair();
            for (int i = 0; i < joints.Length; i++)
                joints[i].localRotation = Quaternion.Slerp(start[i], home[i], Mathf.SmoothStep(0, 1, t / duration));
            yield return null;
        }
        yield return WaitForRepair();
        for (int i = 0; i < joints.Length; i++) joints[i].localRotation = home[i];
    }

    void SetFingers(float open)
    {
        for (int i = 0; i < fingers.Length; i++) fingers[i].localPosition = Vector3.Lerp(fingerHome[i], fingerOpen[i], open);
    }
    IEnumerator Fingers(bool open)
    {
        const float duration=.32f/MotionSpeedMultiplier;
        Vector3[] start=new Vector3[fingers.Length];
        for(int i=0;i<fingers.Length;i++)start[i]=fingers[i].localPosition;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            yield return WaitForRepair();
            float phase=Mathf.SmoothStep(0,1,t/duration);
            for(int i=0;i<fingers.Length;i++)fingers[i].localPosition=Vector3.Lerp(start[i],open?fingerOpen[i]:fingerHome[i],phase);
            yield return null;
        }
        yield return WaitForRepair();
        SetFingers(open ? 1 : 0);
    }

    void OnGUI()
    {
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
        var title = new GUIStyle(GUI.skin.box) { font = uiFont, fontSize = 24, alignment = TextAnchor.UpperLeft, padding = new RectOffset(18, 18, 12, 8) };
        var label = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 21 };
        GUI.Box(new Rect(24, 564, 1120, 132), machineApproach ? "机床上下料演示" : "机械臂抓取演示", title);
        string text = IsBusy ? (Status.StartsWith("Placing") ? "正在放回台面，请稍候…" : "正在抓取并抬起，请稍候…")
            : IsHolding ? "已抓取：再次点击机械臂，将圆柱体放回台面" : "点击机械臂抓取圆柱体，再次点击放下";
        if (Status.Contains("reach")) text = "目标暂时不可达，请再次点击机械臂重试";
        if (machineApproach) text = text.Replace("台面", "机床加工台");
        text = text.Replace("圆柱体", itemDisplayName);
        if (machiningCycle) text = machiningCycle.Message;
        if (IsDamaged) text = "机械臂已损坏，抓放已暂停：再次双击机械臂修复";
        GUI.color = IsDamaged ? new Color(1, .35f, .25f) : Color.white;
        GUI.Label(new Rect(42, 608, 1060, 38), text, label);
        GUI.color = Color.white;
        label.fontSize = 18;
        GUI.Label(new Rect(42, 651, 1060, 35), machiningCycle ? "单击开始加工循环 · 双击损坏/修复 · 右键旋转 · 滚轮缩放 · 中键平移" : "单击抓放 · 双击损坏/修复 · 右键旋转 · 滚轮缩放 · 中键平移 · R 复位视角", label);
        GUI.matrix = previous;
    }
}
