# ShapeGrammar3D

ShapeGrammar3D is a Rhino/Grasshopper plugin for grammar-based generation, structural evaluation, and evolutionary exploration of 3D structural systems. It combines shape grammar rules, a lightweight finite element model, genetic optimization, descriptor extraction, clustering, and preview tools for analyzing families of structural design alternatives.

The plugin is developed as a Grasshopper `.gha` library under the category `StructuralGrammar`.

## Main Capabilities

- Create structural assemblies from curves, elements, supports, loads, materials, and cross-sections.
- Generate structural systems using sequential 2D/3D shape grammar rules.
- Decode genetic chromosomes into grammar derivations.
- Evaluate generated structures using a custom direct-stiffness linear static solver.
- Optimize structural layouts using single-objective GA or multi-objective NSGA-II.
- Perform iterative fully stressed design cross-section sizing.
- Extract topological and geometric descriptors from generated structures.
- Cluster solutions into design families using K-means++.
- Export large optimization runs to JSON and preview Pareto fronts, convergence, metrics, deformations, forces, and selected structures.

## Computational Workflow

The typical optimization workflow is:

1. Define the design domain, supports, loads, materials, sections, and grammar rules in Grasshopper.
2. Assemble an initial `SG_Shape` or create one through `SG_AutoRule_InitShape_3D`.
3. Configure optimization and clustering parameters using `GI Settings`.
4. Run a grammar interpreter such as `GI_FromSgShape`, `GI_LargeSg`, or `GI_Large From SG_Shape`.
5. The interpreter evaluates each individual by:
   - decoding the chromosome into an `SG_Genotype`,
   - applying `SG_Rule` operations to produce an `SG_Shape`,
   - enforcing boundary/support/load constraints,
   - assembling a `TB_Model`,
   - solving the linear static model with `SolveLS`,
   - computing displacement, utilization, feasibility, topology metrics, and shape metrics.
6. The optimizer updates the population using `SG_GA` or `SG_MOGA` (NSGA-II).
7. The population is clustered by weighted topology, shape, and optional fitness descriptors.
8. Results are output as assemblies, JSON runs, Pareto fronts, convergence data, and preview geometry.

## Component Groups

Grasshopper components are organized under `StructuralGrammar`:

| Group | Purpose |
|---|---|
| `01. Material` | Material definitions for structural analysis. |
| `02. Section` | Cross-section definitions and presets. |
| `03. Element` | 1D element creation from lines or curves. |
| `04. Support` | Support condition components. |
| `05. Load` | Point, line, and curve load components. |
| `06. Assembly` | Assembly of elements, supports, loads, and initial shapes. |
| `07. Rules` | Manual and automatic grammar rules. |
| `08. Interpreter` | Grammar interpreters, optimization settings, JSON readers, metric sweeps. |
| `09. Data Preview` | Pareto, radar, convergence, optimization, force, deformation, and table previews. |
| `89. Utilities` | Genotype and helper utilities. |
| `99. Misc` | Miscellaneous tools. |

## Key Classes

| Class | Role |
|---|---|
| `SG_Shape` | Core structural graph containing nodes, elements, supports, loads, and boundary data. |
| `SG_Rule` | Base class for shape grammar operations. |
| `SG_Genotype` | Encodes integer and continuous genes used by grammar rules. |
| `TB_Model` | Finite element representation assembled from an `SG_Shape`. |
| `SolveLS` | Direct-stiffness linear static solver. |
| `StructuralEvaluator` | Applies rules, builds models, solves structures, and computes objectives/descriptors. |
| `SG_GA` | Single-objective genetic algorithm with clustering support. |
| `SG_MOGA` | Multi-objective optimizer using NSGA-II principles. |
| `TopologyMetrics` | Catalogue of graph/topology descriptors. |
| `ShapeMetrics` | Catalogue of geometric/shape descriptors. |
| `FeasibilityMetrics` | Computes normalized feasibility violation metrics. |
| `LargeRunJsonStore` | Streams large optimization runs to JSON. |
| `OptimizationResults` | Lightweight parsed result bundle for preview components. |
| `SGShapeGrammar3DAssembly` | Stores generated shapes, models, individuals, and run metadata. |

## Optimization Objectives

The framework supports one to three objectives.

### Displacement

Structural stiffness is evaluated from the maximum nodal displacement. The objective is expressed as a serviceability displacement ratio using a span-based limit:

```text
dispRatio = maxNodalDisplacement / (span / 300)
```

For multi-objective optimization the displacement objective is stored as:

```text
log(1 + dispRatio)
```

### Utilization

Member utilization is computed from axial force, bending moments, and buckling checks. The utilization objective is controlled by `Util Obj Type`:

- `0`: average utilization deviation from a target value of `0.90` (default).
- `1`: maximum member utilization, minimizing the worst-utilized element.

### Feasibility

Feasibility is evaluated using normalized violation metrics:

- dangling bar penalty,
- small-angle penalty,
- member-length penalty,
- intersection penalty,
- repetitiveness penalty,
- duplicate-element penalty,
- boundary violation ratio.

The weighted feasibility score is stored as `TotalViolation`. In tri-objective NSGA-II mode, the feasibility objective is:

```text
rawFeas = (VDang + VAng + VLen + VBoundary) / 4
```

## Descriptor Metrics

Descriptors are used for clustering and design-family identification.

### Topological Descriptors

- Element count
- Node count
- Element-to-node ratio
- Average node valence
- Maximum node valence
- Leaf node count
- Branch node count
- Euler characteristic
- Distinct element names
- Support count
- Connected components
- Cycle rank
- Maximum pipe intersections
- Average pipe intersections

### Shape Descriptors

- Total length
- Average member length
- Maximum member length
- Minimum member length
- Standard deviation of member length
- Bounding-box volume
- Bounding-box diagonal
- Total structural volume
- Maximum node span
- Compactness
- Convex hull area in XY
- Hull aspect ratio in XY
- Mesh area from lines
- Convex hull volume
- Shrink-wrap volume

## Cross-Section Optimization

Cross-section sizing uses an iterative fully stressed design (FSD) strategy. The implemented modes include:

- solid rectangular sections,
- SHS catalogue sections,
- additional catalogue modes exposed in the settings component.

The sizing loop searches for sections that approach a target utilization of approximately `0.90`, with safety adjustments for members exceeding utilization limits.

## Large Run Workflow

For large optimization runs, use the large-data interpreters:

- `GI_LargeSg`
- `GI_Large From SG_Shape`

These components stream results to JSON through `LargeRunJsonStore` and output a `LargeRunContext`. The JSON can be read with `GI_LargeJson Reader`, which produces:

- run information,
- `OptimizationResults`,
- optional rebuilt `SGShapeGrammar3DAssembly`.

The `GI_Opti Preview` component can then display:

- 3D Pareto front,
- 2D objective projections,
- convergence curves,
- cluster colouring,
- bakeable axis labels,
- optional miniature structural previews from an assembly.

## Build Requirements

- Windows
- Rhino 8 / Grasshopper 8
- .NET SDK compatible with `net7.0-windows`

The project targets:

```xml
net7.0-windows
```

Main dependencies:

- `Grasshopper` NuGet package
- `CSparse`
- `RhinoCommon` / Grasshopper runtime provided by Rhino

## Build and Install

From the repository root:

```powershell
dotnet build .\ShapeGrammar3D\ShapeGrammar3D.csproj
```

After a successful build, the project copies the plugin to:

```text
%APPDATA%\Grasshopper\Libraries\ShapeGrammar3D
```

The output plugin file is:

```text
ShapeGrammar3D.gha
```

If Rhino or Grasshopper already has the plugin loaded, the post-build copy can fail because the `.gha` file is locked. In that case, close Rhino or build without deployment:

```powershell
dotnet build .\ShapeGrammar3D\ShapeGrammar3D.csproj /p:SkipGrasshopperDeploy=true
```

Then copy the output manually after Rhino is closed.

## Data Files

The project includes:

```text
ShapeGrammar3D\Data\SHS_Catalog.csv
```

This catalogue is embedded and copied to the output directory for square hollow section sizing.

## Notes on Karamba3D

The optimization loop uses the internal `SolveLS` direct-stiffness solver. Karamba3D is not required for the main optimization workflow. The component `Model->Karamba` decomposes `TB_Model` data into Karamba-ready lines, supports, loads, materials, and sections for optional interoperability.

## Suggested Academic References

For NSGA-II:

> Deb, K., Pratap, A., Agarwal, S., & Meyarivan, T. (2002). A fast and elitist multiobjective genetic algorithm: NSGA-II. IEEE Transactions on Evolutionary Computation, 6(2), 182-197. https://doi.org/10.1109/4235.996017

For fully stressed design cross-section sizing:

> Ahrari, A., & Atai, A. A. (2013). Fully Stressed Design Evolution Strategy for Shape and Size Optimization of Truss Structures. Computers & Structures, 123, 58-67. https://doi.org/10.1016/j.compstruc.2013.04.013

## Repository Structure

```text
ShapeGrammar3D/
  Components/              Grasshopper components
  Classes/                 Core data structures, rules, solvers, metrics, optimization
  Data/                    Section catalogues
  Properties/              Icons and embedded resources
docs/figures/              Research figures and Mermaid/SVG outputs
```

## Status

This repository is an active research plugin. Component names and data schemas may evolve as the grammar, optimization, and preview workflows are extended.
