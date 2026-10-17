
import bpy, bmesh, math, numpy as np
from collections import deque, Counter
from mathutils import Quaternion, Vector
REGIONS={"armL":["LeftShoulder","LeftArm","LeftForeArm","LeftHand"],"armR":["RightShoulder","RightArm","RightForeArm","RightHand"],
 "legL":["LeftUpLeg","LeftLeg","LeftFoot","LeftToeBase"],"legR":["RightUpLeg","RightLeg","RightFoot","RightToeBase"],
 "core":["Hips","Spine","Spine1","Neck","Head"]}
def segd(arm,n,X):
    b=arm.data.bones[n]; a=np.array(b.head_local); c=np.array(b.tail_local); ab=c-a
    t=np.clip(((X-a)@ab)/(ab@ab),0,1); return np.linalg.norm(X-(a+t[:,None]*ab),axis=1)
def neighbours(ob):
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.verts.ensure_lookup_table()
    nb=[[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]; bm.free(); return nb
def label(ob,arm,seed_r,forced=None):
    """Geodesic region labels: seeds near each region's bones, grown along the mesh surface."""
    co=np.array([v.co[:] for v in ob.data.vertices]); N=len(co); keys=list(REGIONS)
    D={k:np.min(np.stack([segd(arm,b,co) for b in REGIONS[k]],1),1) for k in keys}
    lab=-np.ones(N,int); q=deque()
    order=np.argsort(np.min(np.stack([D[k]/seed_r[k] for k in keys],1),1))
    for i in order:
        k=min(keys,key=lambda kk:D[kk][i]/seed_r[kk])
        if D[k][i]<seed_r[k]: lab[i]=keys.index(k); q.append(i)
    if forced:
        for k,m in forced.items():
            for i in np.nonzero(m)[0]: lab[i]=keys.index(k)
            q.extend(np.nonzero(m)[0].tolist())
    nb=neighbours(ob)
    while q:
        i=q.popleft()
        for j in nb[i]:
            if lab[j]<0: lab[j]=lab[i]; q.append(j)
    for i in np.nonzero(lab<0)[0]: lab[i]=keys.index(min(keys,key=lambda kk:D[kk][i]))
    return lab,keys,co
def region_weights(ob,arm,lab,keys,co,blend_r=0.12,rigid=None):
    names=[b.name for b in arm.data.bones]; gi={n:i for i,n in enumerate(names)}
    W=np.zeros((len(co),len(names)),np.float32)
    def sstep(v): v=np.clip(v,0,1); return v*v*(3-2*v)
    for li,k in enumerate(keys):
        idx=np.nonzero(lab==li)[0]
        if not len(idx): continue
        bl=REGIONS[k]; Dd=np.stack([segd(arm,b,co[idx]) for b in bl],1); w=1/np.maximum(Dd,0.02)**3
        o=np.argsort(-w,1); m=np.zeros_like(w,bool)
        for j in range(min(2,len(bl))): m[np.arange(len(idx)),o[:,j]]=True
        w*=m; w/=w.sum(1,keepdims=True)
        bl_w=np.zeros(len(idx))
        if k!="core":
            root=arm.data.bones[bl[0] if k.startswith("leg") else bl[1]]
            dh=np.linalg.norm(co[idx]-np.array(root.head_local),axis=1); bl_w=1-sstep(dh/blend_r)
        for j,b in enumerate(bl): W[idx,gi[b]]=w[:,j]*(1-bl_w)
        if k!="core":
            par="Hips" if k.startswith("leg") else "Spine1"
            W[idx,gi[par]]+=bl_w
    if rigid:
        for bone,m in rigid.items(): W[m]=0; W[m,gi[bone]]=1
    ob.vertex_groups.clear()
    for k,n in enumerate(names):
        g=ob.vertex_groups.new(name=n)
        for i in np.nonzero(W[:,k]>0.003)[0]: g.add([int(i)],float(W[i,k]),'REPLACE')
def split_regions(ob,lab,keys,keep_pairs=()):
    """Cut faces between regions that must move independently (arm vs body etc)."""
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    keepset={frozenset(p) for p in keep_pairs}
    fl=[Counter(lab[v.index] for v in f.verts).most_common(1)[0][0] for f in bm.faces]
    bm.edges.ensure_lookup_table(); cut=[]
    for e in bm.edges:
        ls={fl[f.index] for f in e.link_faces}
        if len(ls)>1:
            names={keys[l] for l in ls}
            if frozenset(names) not in keepset: cut.append(e)
    newlab=list(lab)
    bmesh.ops.split_edges(bm,edges=cut)
    bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    out=np.zeros(len(bm.verts),int)
    for f in bm.faces:
        for v in f.verts: out[v.index]=fl[f.index]
    for v in bm.verts:
        if not v.link_faces: out[v.index]=lab[v.index] if v.index<len(lab) else 0
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return out,len(cut)
def stretch(ob,arm,poses):
    rest=np.array([v.co[:] for v in ob.data.vertices]); E=np.array([e.vertices[:] for e in ob.data.edges]); rl=np.linalg.norm(rest[E[:,0]]-rest[E[:,1]],axis=1)
    res=[]
    for name,pose in poses.items():
        for pb in arm.pose.bones: pb.rotation_mode='QUATERNION'; pb.rotation_quaternion=(1,0,0,0)
        for bn,ax,deg in pose:
            pb=arm.pose.bones[bn]; B=pb.bone.matrix_local.to_3x3()
            pb.rotation_quaternion=(B.inverted()@Quaternion(Vector(ax),math.radians(deg)).to_matrix()@B).to_quaternion()@pb.rotation_quaternion
        bpy.context.view_layer.update()
        dg=bpy.context.evaluated_depsgraph_get(); ev=ob.evaluated_get(dg); me=ev.to_mesh(); pc=np.array([v.co[:] for v in me.vertices]); ev.to_mesh_clear()
        g=np.linalg.norm(pc[E[:,0]]-pc[E[:,1]],axis=1)-rl; res.append((name,int((g>0.06).sum()),round(float(g.max()),2)))
    return res
POSES_MAGE={"stride":[("LeftUpLeg",(1,0,0),-30),("LeftLeg",(1,0,0),20),("RightUpLeg",(1,0,0),25),("RightLeg",(1,0,0),40)],
 "cast":[("LeftArm",(1,0,0),-75),("LeftForeArm",(1,0,0),-25),("RightArm",(1,0,0),25),("RightForeArm",(1,0,0),-30),("Spine1",(0,0,1),-10)],
 "armsdown":[("LeftArm",(0,1,0),30),("RightArm",(0,1,0),-30)]}
