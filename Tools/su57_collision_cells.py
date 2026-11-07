"""Produce disjoint structural collision cells in the authored rest pose.

Only physics meshes are inset. Visible surfaces keep their original positions.
"""
import math
from prepare_su57_damage_sections import add, sub, mul, dot, cross, normalize, convex_shell, vec


def clip_hull(polygons, normal, distance):
    output = []
    intersections = {}
    has_inside = has_outside = False
    for polygon in polygons:
        clipped = []
        for a, b in zip(polygon, polygon[1:] + polygon[:1]):
            da, db = dot(normal, a) - distance, dot(normal, b) - distance
            has_inside = has_inside or da < -1e-9
            has_outside = has_outside or da > 1e-9
            if da <= 1e-9:
                clipped.append(a)
                if abs(da) <= 1e-9:
                    intersections[tuple(round(c, 8) for c in a)] = a
            if (da < -1e-9 and db > 1e-9) or (db < -1e-9 and da > 1e-9):
                p = add(a, mul(sub(b, a), da / (da - db)))
                clipped.append(p)
                intersections[tuple(round(c, 8) for c in p)] = p
        if len(clipped) >= 3:
            output.append(clipped)
    if has_inside and has_outside and len(intersections) >= 3:
        points = list(intersections.values())
        center = mul(tuple(map(sum, zip(*points))), 1 / len(points))
        axis = min([(1, 0, 0), (0, 1, 0), (0, 0, 1)], key=lambda a: abs(dot(a, normal)))
        u = normalize(cross(axis, normal))
        v = cross(normal, u)
        points.sort(key=lambda p: math.atan2(dot(sub(p, center), v), dot(sub(p, center), u)))
        output.append(points)
    return output


def hull_polygons(shell):
    vertices=[tuple(p[a] for a in 'xyz') for p in shell['vertices']]
    t=shell['triangles']
    return [[vertices[i] for i in t[j:j+3]] for j in range(0,len(t),3)]


def planes(polygons):
    unique={}
    for p in polygons:
        # Cap contours can start with collinear points after clipping; use
        # the polygon area vector instead of the first three vertices.
        n=normalize(tuple(map(sum,zip(*(cross(a,b) for a,b in zip(p,p[1:]+p[:1]))))))
        if dot(n,n)<.5:continue
        d=dot(n,p[0]);unique[tuple(round(c,7) for c in (*n,d))]=(n,d)
    return list(unique.values())


def volume(polygons):
    return abs(sum(dot(p[0],cross(p[i],p[i+1])) for p in polygons for i in range(1,len(p)-1)))/6


def separate_shells(shells, gap=.024):
    # Inset the ORIGINAL face planes, retaining their joint boundaries.
    # Do not globally assign hulls by their center bisectors: that would cut
    # away source regions even where the original hulls never intersected.
    cells=[]
    for shell in shells:
        original=hull_polygons(shell);cell=original
        for n,d in planes(original):cell=clip_hull(cell,n,d-.006)
        if not cell or volume(cell)<1e-9:
            raise ValueError('Cannot inset structural collision shell: '+shell['partPath'])
        cells.append(cell)
    for i in range(len(cells)):
        for j in range(i+1,len(cells)):
            a,b=cells[i],cells[j]
            av=[p for face in a for p in face];bv=[p for face in b for p in face]
            if any(min(p[k] for p in av)>=max(p[k] for p in bv)-1e-8 or
                   min(p[k] for p in bv)>=max(p[k] for p in av)-1e-8 for k in range(3)):continue
            intersection=a
            for n,d in planes(b):
                intersection=clip_hull(intersection,n,d)
                if not intersection:break
            if not intersection or volume(intersection)<1e-8:continue
            ac=mul(tuple(map(sum,zip(*av))),1/len(av))
            bc=mul(tuple(map(sum,zip(*bv))),1/len(bv))
            delta=sub(bc,ac)
            if dot(delta,delta)<gap*gap:
                raise ValueError('Overlapping structural cells have coincident centers')
            n=normalize(delta)
            alo,ahi=min(dot(n,p) for p in av),max(dot(n,p) for p in av)
            blo,bhi=min(dot(n,p) for p in bv),max(dot(n,p) for p in bv)
            # Resolve only a real overlap, placing the divider within the
            # support overlap when possible and retaining each interior point.
            split=(max(alo,blo)+min(ahi,bhi))/2
            split=max(dot(n,ac)+gap/2+1e-6,min(split,dot(n,bc)-gap/2-1e-6))
            cells[i]=clip_hull(a,n,split-gap/2)
            cells[j]=clip_hull(b,mul(n,-1),-split-gap/2)
    for shell,polygons in zip(shells,cells):
        # A re-hull using a subset of these vertices cannot expand the result.
        shell.update(convex_shell([p for face in polygons for p in face],min_thickness=0))
    return shells
