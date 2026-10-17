
import bpy, bmesh, numpy as np
from mathutils import Vector
def build_humanoid(mesh_name, J, rig_name):
    """J: joint positions (world, metres). Keys: hips, spine, chest, neck, head, headtop, and per side
    (L/R): shoulder, arm, elbow, wrist, hand_end, upleg, knee, ankle, toe, toe_end."""
    ob=bpy.data.objects[mesh_name]
    ad=bpy.data.armatures.new(rig_name); arm=bpy.data.objects.new(rig_name, ad)
    bpy.context.scene.collection.objects.link(arm); arm.show_in_front=True; ad.display_type='STICK'
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active=arm; arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT'); eb=ad.edit_bones
    def B(n,h,t,p=None,conn=False):
        b=eb.new(n); b.head=Vector(h); b.tail=Vector(t)
        if p: b.parent=eb[p]; b.use_connect=conn
        return b
    B("Hips",J["hips"],J["spine"])
    B("Spine",J["spine"],J["chest"],"Hips",True)
    B("Spine1",J["chest"],J["neck"],"Spine",True)
    B("Neck",J["neck"],J["head"],"Spine1",True)
    B("Head",J["head"],J["headtop"],"Neck",True)
    for s,side in (("L","Left"),("R","Right")):
        g=lambda k: J[k+"_"+s]
        B(side+"Shoulder",g("shoulder"),g("arm"),"Spine1")
        B(side+"Arm",g("arm"),g("elbow"),side+"Shoulder",True)
        B(side+"ForeArm",g("elbow"),g("wrist"),side+"Arm",True)
        B(side+"Hand",g("wrist"),g("hand_end"),side+"ForeArm",True)
        B(side+"UpLeg",g("upleg"),g("knee"),"Hips")
        B(side+"Leg",g("knee"),g("ankle"),side+"UpLeg",True)
        B(side+"Foot",g("ankle"),g("toe"),side+"Leg",True)
        B(side+"ToeBase",g("toe"),g("toe_end"),side+"Foot",True)
    bpy.ops.armature.select_all(action='SELECT'); bpy.ops.armature.calculate_roll(type='GLOBAL_POS_Y')
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm
def bind(mesh_name, rig_name):
    ob=bpy.data.objects[mesh_name]; arm=bpy.data.objects[rig_name]
    for o in bpy.context.selected_objects: o.select_set(False)
    ob.select_set(True); arm.select_set(True); bpy.context.view_layer.objects.active=arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    W=weights(ob); return int((W.sum(1)<1e-3).sum())
def weights(ob):
    names=[g.name for g in ob.vertex_groups]; W=np.zeros((len(ob.data.vertices),len(names)),np.float32)
    for v in ob.data.vertices:
        for g in v.groups: W[v.index,g.group]=g.weight
    return W
def set_rigid(ob, mask, bone):
    """Every vertex in mask follows one bone only."""
    idx=[int(i) for i in np.nonzero(mask)[0]]
    for g in ob.vertex_groups: g.remove(idx)
    if bone not in ob.vertex_groups: ob.vertex_groups.new(name=bone)
    ob.vertex_groups[bone].add(idx,1.0,'REPLACE'); return len(idx)
def strip(ob, mask, bones):
    """Remove the given bones' influence from the masked verts and renormalize."""
    names=[g.name for g in ob.vertex_groups]; W=weights(ob)
    cols=[names.index(b) for b in bones if b in names]
    for i in np.nonzero(mask)[0]:
        if W[i,cols].sum()<=0: continue
        W[i,cols]=0; s=W[i].sum()
        if s<=0:
            W[i,nearest_allowed(ob,i,names,set(bones))]=1.0; s=1.0
        W[i]/=s
        for k,n in enumerate(names):
            g=ob.vertex_groups[n]
            if W[i,k]>0.001: g.add([int(i)],float(W[i,k]),'REPLACE')
            else: g.remove([int(i)])
def fill_unweighted(ob):
    import bmesh
    from collections import deque
    W=weights(ob); ok=W.sum(1)>1e-3; N=len(W)
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.verts.ensure_lookup_table()
    nbr=[[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]; bm.free()
    src=-np.ones(N,int); q=deque(np.nonzero(ok)[0].tolist())
    for i in q: src[i]=i
    while q:
        i=q.popleft()
        for j in nbr[i]:
            if src[j]<0: src[j]=src[i]; q.append(j)
    names=[g.name for g in ob.vertex_groups]; n=0
    for i in np.nonzero(~ok)[0]:
        if src[i]<0: continue
        for k,nm in enumerate(names):
            if W[src[i],k]>0.001: ob.vertex_groups[nm].add([int(i)],float(W[src[i],k]),'REPLACE')
        n+=1
    return n

def proxy_weights(mesh_name, rig_name, voxel=0.012):
    import bmesh
    from collections import deque
    from mathutils.bvhtree import BVHTree
    ob=bpy.data.objects[mesh_name]; arm=bpy.data.objects[rig_name]
    ob.vertex_groups.clear()
    px=ob.copy(); px.data=ob.data.copy(); px.name=mesh_name+"_Proxy"; bpy.context.scene.collection.objects.link(px)
    px.modifiers.clear(); px.parent=None; px.matrix_world=ob.matrix_world.copy()
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active=px; px.select_set(True)
    r=px.modifiers.new("R","REMESH"); r.mode='VOXEL'; r.voxel_size=voxel; bpy.ops.object.modifier_apply(modifier="R")
    distance_weights(px, arm)
    for o in bpy.context.selected_objects: o.select_set(False)
    bpy.context.view_layer.objects.active=px; px.select_set(True)
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.5, repeat=3)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    fill_unweighted(px)
    names=[b.name for b in arm.data.bones]
    pn=[g.name for g in px.vertex_groups]; Wp=weights(px)
    pb=bmesh.new(); pb.from_mesh(px.data); pb.faces.ensure_lookup_table(); bvh=BVHTree.FromBMesh(pb)
    pco=np.array([v.co[:] for v in px.data.vertices])
    N=len(ob.data.vertices); W=np.zeros((N,len(pn)),np.float32); dist=np.zeros(N)
    for v in ob.data.vertices:
        loc,nrm,fi,d=bvh.find_nearest(v.co); vs=[x.index for x in pb.faces[fi].verts]
        dd=np.linalg.norm(pco[vs]-np.array(loc),axis=1)+1e-6; w=(1/dd)/(1/dd).sum()
        W[v.index]=(Wp[vs]*w[:,None]).sum(0); dist[v.index]=d
    pb.free()
    # protruding verts copy their nearest attached neighbour along the mesh
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.verts.ensure_lookup_table()
    nbr=[[e.other_vert(v).index for e in v.link_edges] for v in bm.verts]; bm.free()
    anch=(dist<voxel*2.5)&(W.sum(1)>1e-3); src=-np.ones(N,int); q=deque(np.nonzero(anch)[0].tolist())
    for i in q: src[i]=i
    while q:
        i=q.popleft()
        for j in nbr[i]:
            if src[j]<0: src[j]=src[i]; q.append(j)
    m=(~anch)&(src>=0); W[m]=W[src[m]]
    s=W.sum(1,keepdims=True); s[s==0]=1; W/=s
    for k,n in enumerate(pn):
        g=ob.vertex_groups.new(name=n); idx=np.nonzero(W[:,k]>0.003)[0]
        for i in idx: g.add([int(i)],float(W[i,k]),'REPLACE')
    if not any(md.type=='ARMATURE' for md in ob.modifiers):
        md=ob.modifiers.new("Armature","ARMATURE"); md.object=arm
    ob.parent=arm
    bpy.data.objects.remove(px,do_unlink=True)
    return int(m.sum()), int((W.sum(1)<1e-3).sum())

def distance_weights(px, arm, power=4.0, topk=3):
    P=np.array([v.co[:] for v in px.data.vertices])
    bones=[b for b in arm.data.bones]
    def segd(b):
        a=np.array(b.head_local); c=np.array(b.tail_local); ab=c-a
        t=np.clip(((P-a)@ab)/(ab@ab),0,1); return np.linalg.norm(P-(a+t[:,None]*ab),axis=1)
    D=np.stack([segd(b) for b in bones],1)
    for k,b in enumerate(bones):
        if b.name.startswith("Left"): D[P[:,0]<-0.02,k]=9
        if b.name.startswith("Right"): D[P[:,0]>0.02,k]=9
        if b.name in ("Head","Neck"): D[:,k]*=1.15
    w=1/np.maximum(D,0.01)**power
    o=np.argsort(-w,1); m=np.zeros_like(w,bool)
    for k in range(topk): m[np.arange(len(P)),o[:,k]]=True
    w=w*m; w/=w.sum(1,keepdims=True)
    px.vertex_groups.clear()
    for k,b in enumerate(bones):
        g=px.vertex_groups.new(name=b.name); idx=np.nonzero(w[:,k]>0.003)[0]
        for i in idx: g.add([int(i)],float(w[i,k]),'REPLACE')

def detach(ob, mask):
    """Cut every edge between masked and unmasked verts so a rigid prop (weapon) can't drag the body along."""
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.edges.ensure_lookup_table()
    cut=[e for e in bm.edges if mask[e.verts[0].index]!=mask[e.verts[1].index]]
    # split the verts on the masked side away from faces on the other side
    vs=set()
    for e in cut:
        for v in e.verts:
            if mask[v.index]: vs.add(v)
    n=len(bm.verts)
    bmesh.ops.split_edges(bm, edges=cut)
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return len(cut), len(ob.data.vertices)-n
def strip_far(ob, arm, bone_names, keep_mask, radius):
    co=np.array([v.co[:] for v in ob.data.vertices])
    def segd(n):
        b=arm.data.bones[n]; a=np.array(b.head_local); c=np.array(b.tail_local); ab=c-a
        t=np.clip(((co-a)@ab)/(ab@ab),0,1); return np.linalg.norm(co-(a+t[:,None]*ab),axis=1)
    d=np.min(np.stack([segd(n) for n in bone_names],1),1)
    m=(d>radius)&~keep_mask
    strip(ob,m,bone_names); return int(m.sum())

def limb_of(name):
    for side in ("Left","Right"):
        if name.startswith(side):
            rest=name[len(side):]
            if rest in ("Arm","ForeArm","Hand"): return "arm"+side
            if rest in ("UpLeg","Leg","Foot","ToeBase"): return "leg"+side
    return "core"
def detach_limbs(ob):
    W=weights(ob); names=[g.name for g in ob.vertex_groups]
    dom=np.array([limb_of(names[k]) for k in W.argmax(1)])
    bad={frozenset(p) for p in [("armLeft","legLeft"),("armLeft","legRight"),("armRight","legLeft"),("armRight","legRight"),("legLeft","legRight"),("armLeft","armRight")]}
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.edges.ensure_lookup_table()
    cut=[e for e in bm.edges if dom[e.verts[0].index]!=dom[e.verts[1].index] and frozenset((dom[e.verts[0].index],dom[e.verts[1].index])) in bad]
    bmesh.ops.split_edges(bm, edges=cut); bm.to_mesh(ob.data); bm.free(); ob.data.update()
    return len(cut)
def stretch_report(ob, thresh=0.08):
    rest=np.array([v.co[:] for v in ob.data.vertices]); E=np.array([e.vertices[:] for e in ob.data.edges]); rl=np.linalg.norm(rest[E[:,0]]-rest[E[:,1]],axis=1)
    dg=bpy.context.evaluated_depsgraph_get(); ev=ob.evaluated_get(dg); me=ev.to_mesh(); pc=np.array([v.co[:] for v in me.vertices]); ev.to_mesh_clear()
    pl=np.linalg.norm(pc[E[:,0]]-pc[E[:,1]],axis=1); return int(((pl-rl)>thresh).sum()), round(float((pl-rl).max()),2)

def nearest_allowed(ob, i, names, banned):
    arm=ob.find_armature() or (ob.parent if ob.parent and ob.parent.type=='ARMATURE' else None)
    if arm is None:
        arm=[o for o in bpy.data.objects if o.type=='ARMATURE' and o.name.startswith(ob.name)][0]
    p=np.array(ob.data.vertices[i].co); best=None; bd=9
    for k,n in enumerate(names):
        if n in banned or n not in arm.data.bones: continue
        b=arm.data.bones[n]; a=np.array(b.head_local); c=np.array(b.tail_local); ab=c-a
        t=np.clip(np.dot(p-a,ab)/np.dot(ab,ab),0,1); d=np.linalg.norm(p-(a+t*ab))
        if d<bd: bd=d; best=k
    return best
def detach_faces(ob):
    """Separate faces by limb so arm geometry and leg geometry never share a vertex."""
    W=weights(ob); names=[g.name for g in ob.vertex_groups]
    dom=[limb_of(names[k]) for k in W.argmax(1)]
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.faces.ensure_lookup_table()
    from collections import Counter
    groups={}
    for f in bm.faces:
        g=Counter(dom[v.index] for v in f.verts).most_common(1)[0][0]
        groups.setdefault(g,[]).append(f)
    for g,fs in groups.items():
        if g=="core": continue
        bmesh.ops.split(bm, geom=fs, use_only_faces=False)
    bm.to_mesh(ob.data); bm.free(); ob.data.update()
    # faces now own their verts; snap each vertex's weights to its face group's limb
    W=weights(ob); names=[g.name for g in ob.vertex_groups]
    bm=bmesh.new(); bm.from_mesh(ob.data); bm.verts.ensure_lookup_table()
    fixed=0
    for v in bm.verts:
        if not v.link_faces: continue
        g=Counter(limb_of(names[int(W[u.index].argmax())]) for f in v.link_faces for u in f.verts).most_common(1)[0][0]
        mine=limb_of(names[int(W[v.index].argmax())])
        if mine!=g and frozenset((mine,g)) in BAD:
            nb=[u.index for f in v.link_faces for u in f.verts if limb_of(names[int(W[u.index].argmax())])==g]
            if nb:
                w=W[nb].mean(0); w/=w.sum()
                for k,n in enumerate(names):
                    grp=ob.vertex_groups[n]
                    if w[k]>0.001: grp.add([v.index],float(w[k]),'REPLACE')
                    else: grp.remove([v.index])
                fixed+=1
    bm.free(); return len(groups), fixed
BAD={frozenset(p) for p in [("armLeft","legLeft"),("armLeft","legRight"),("armRight","legLeft"),("armRight","legRight"),("legLeft","legRight"),("armLeft","armRight")]}
