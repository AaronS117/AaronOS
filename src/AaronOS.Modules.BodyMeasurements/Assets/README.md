# Body figure asset

## `human-base.mesh`

Derived from the **MakeHuman community base mesh** (`makehuman/data/3dobjs/base.obj`) with MakeHuman's
own young-male morph targets applied, all taken from
<https://github.com/makehumancommunity/makehuman>.

**Licence: CC0 1.0 (public domain dedication).** Both the mesh and the targets state in their own
headers that the asset "was explicitly released as CC0 in september 2020". No attribution is required;
it is recorded here so the provenance of a binary asset is not lost.

### The male morph

MakeHuman's base mesh is a neutral androgynous form — it reads as female if used unmodified, which is
not a usable default for this app. A recognisable body comes from applying MakeHuman's morph targets,
so the asset is baked with these three at equal weight:

- `data/targets/macrodetails/african-male-young.target`
- `data/targets/macrodetails/asian-male-young.target`
- `data/targets/macrodetails/caucasian-male-young.target`

An equal blend is MakeHuman's own behaviour when the ethnicity sliders are untouched, which yields a
male figure without baking in an ethnicity the app never asks about.

The muscle and weight axis is deliberately left alone. MakeHuman's
`universal-male-young-averagemuscle-averageweight.target` contains no offsets at all, because average
build *is* the reference pose — muscle and fat come from the measurements recorded in the app, not
from a preset. Supporting a female figure later means baking a second asset from the corresponding
`*-female-young` targets and choosing between them; nothing in the runtime is male-specific.

This licence is why the mesh is MakeHuman's rather than one of the more detailed parametric human
models available. Most published human body models — anything derived from SMPL, including the
majority of those on Hugging Face — are licensed for non-commercial research only, forbid
redistribution, and require registration. None of that is compatible with a public repository.

### What the pre-processing does

The upstream `base.obj` is 1.75 MB of quads across 172 groups, most of which are `joint-*` and
`helper-*` proxies that MakeHuman uses internally. The build script keeps only the `body` group and:

- triangulates the quads and drops unreferenced vertices, leaving 13,380 vertices and 26,756
  triangles (small enough for `ushort` indices, which halves the asset's index data);
- normalises the figure to unit height with the feet at Y = 0 and centred on X and Z, so the runtime
  scales it by a person's height and nothing else;
- splits it into the regions a tape measure names — torso, neck, each arm, each leg — and stores a
  per-vertex blend weight for each, feathered across the mesh so regions merge smoothly;
- fits an axis to each region and records the region's true circumference sampled along that axis,
  measured by slicing the mesh with a plane and summing the cross-section's perimeter.

`BodyMeshDeformer` then only has to scale each vertex away from its region's axis by the ratio of a
recorded measurement to the base girth at the same place. All the analysis is offline; start-up reads
numbers.

Two properties of the mesh are worth knowing, because the code depends on them: the arms hang forward
and down in a relaxed A-pose, and the legs are splayed, with the right leg's centre travelling from
x = 0.064 at the hip to x = 0.134 at the foot. Limb axes are therefore fitted, not assumed vertical.

### Regenerating it

The build script is not checked in, as it is a one-time transform of an external asset and needs the
1.75 MB upstream `base.obj`. The format is versioned (`AOSM`, version 3) and `HumanBaseMesh` rejects
any version it does not understand, so a stale asset fails loudly at start-up rather than rendering
something wrong.
