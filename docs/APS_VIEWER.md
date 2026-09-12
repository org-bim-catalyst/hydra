The **Autodesk Platform Services (APS) Viewer** is much more than a 3D model renderer. It provides a programmable web-based viewer framework where you can customize the UI, add tools, create panels, interact with model geometry, and build your own BIM/engineering workflows on top of Autodesk models.

### Core APS Viewer capabilities

| Capability                   | What you can do                                                                                                |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------- |
| **3D/2D Model Viewing**      | Display Revit, Navisworks, IFC and many other supported formats after translation through APS Model Derivative |
| **Viewer Toolbar**           | Use the built-in navigation, selection, sectioning, measurement, markup and viewing tools                      |
| **Custom Toolbar Buttons**   | Add your own buttons, icons, dropdowns and controls to the Viewer toolbar                                      |
| **Viewer Extensions**        | Load Autodesk or custom JavaScript extensions that add functionality to the Viewer                             |
| **Custom Panels**            | Create dockable/floating panels, property panels, dialogs and other HTML-based UI                              |
| **Overlay UI**               | Place custom HTML/CSS interfaces over the Viewer without modifying the underlying model                        |
| **Model Browser**            | Access and customize the model hierarchy/tree and create custom object navigation                              |
| **Properties**               | Retrieve and display properties of selected model elements                                                     |
| **Object Selection**         | Select objects and retrieve their database IDs, properties and geometry information                            |
| **Geometry Interaction**     | Highlight, isolate, hide/show, focus, fit-to-view and manipulate model objects                                 |
| **Camera Control**           | Programmatically control position, target, orientation, projection and navigation                              |
| **Sections**                 | Create section planes and section boxes and control them programmatically                                      |
| **Measurements**             | Distance, area and other measurement functionality, including custom measurement workflows                     |
| **Markup**                   | Add annotations, dimensions, clouds, arrows, text and other markup capabilities                                |
| **2D Drawings**              | View sheets/drawings and interact with their underlying objects                                                |
| **Model Data**               | Access object properties, hierarchy, metadata and relationships                                                |
| **Multi-Model Viewing**      | Load and coordinate multiple models into a single Viewer scene                                                 |
| **Model Aggregation**        | Combine architectural, structural, MEP, infrastructure or other disciplines                                    |
| **Events**                   | Respond to selection, camera movement, model loading, toolbar interaction and other Viewer events              |
| **Custom Tools**             | Create completely custom mouse/keyboard interaction tools                                                      |
| **Rendering**                | Control materials, visibility, colors, transparency and visual appearance                                      |
| **Overlays**                 | Create custom WebGL overlay scenes for graphics, markers, paths, particles, guides, etc.                       |
| **World/Screen Coordinates** | Convert between model coordinates and screen coordinates for custom UI and visualization                       |
| **Data Visualization**       | Color-code model elements, create heatmaps, status visualization, dashboards, etc.                             |
| **Search**                   | Search model elements by properties, categories, names, IDs, etc.                                              |
| **Selection Sets**           | Create and manipulate groups of selected objects                                                               |
| **Property-Based Filtering** | Find objects based on Revit/BIM properties and metadata                                                        |
| **Custom Context Menus**     | Add actions to right-click/context menus                                                                       |
| **Custom UI**                | Build your own HTML/CSS/JS interface around or inside the Viewer                                               |
| **Localization**             | Support localized/custom UI strings                                                                            |
| **Theming**                  | Customize Viewer appearance and integrate it with your application's design system                             |

### The important architectural idea

You can think of APS Viewer as having **four layers**:

```text
┌──────────────────────────────────────────────┐
│              YOUR APPLICATION                │
│                                              │
│  React / Angular / Vue / ASP.NET / Vanilla   │
│                                              │
├──────────────────────────────────────────────┤
│             CUSTOM VIEWER UI                 │
│                                              │
│  Panels • Dialogs • Toolbars • Menus         │
│  Dashboards • Forms • Property Editors       │
│                                              │
├──────────────────────────────────────────────┤
│             APS VIEWER API                   │
│                                              │
│  Extensions • Tools • Events • Selection     │
│  Camera • Model Data • Geometry • Overlays   │
│                                              │
├──────────────────────────────────────────────┤
│               WEBGL ENGINE                   │
│                                              │
│       Autodesk Viewer Rendering Engine       │
│                                              │
└──────────────────────────────────────────────┘
```

This is particularly powerful because **your application doesn't have to look like Autodesk's Viewer**. You can use the Viewer as the 3D/2D visualization engine while completely surrounding it with your own SaaS interface.

### Extensions

Extensions are probably the most important customization mechanism.

An extension can:

* Add toolbar buttons
* Add panels
* Add menus
* Register custom tools
* Listen to Viewer events
* Access model data
* Modify visibility
* Create overlays
* Add custom graphics
* Interact with selected objects
* Communicate with your application's backend
* Add entirely new workflows

Conceptually:

```javascript
class MyExtension extends Autodesk.Viewing.Extension {

    load() {
        // Create UI
        // Register events
        // Initialize custom tools

        return true;
    }

    unload() {
        // Remove UI
        // Remove event listeners
        // Cleanup

        return true;
    }
}
```

Then:

```javascript
viewer.loadExtension("MyExtension");
```

This makes an extension behave almost like a **plugin system for your web application**.

### Custom panels

You can create UI such as:

```text
┌─────────────────────────────────────────────────────────────┐
│ APS Viewer Toolbar                                          │
├───────────────┬─────────────────────────────────────────────┤
│               │                                             │
│  Model        │                                             │
│  Browser      │             3D MODEL                       │
│               │                                             │
│  ├─ Building  │                                             │
│  ├─ Level 01  │                                             │
│  ├─ Level 02  │                                             │
│  └─ Level 03  │                                             │
│               │                                             │
├───────────────┴─────────────────────────────────────────────┤
│                  Custom Bottom Panel                        │
└─────────────────────────────────────────────────────────────┘
```

Or build something much more SaaS-like:

```text
┌─────────────────────────────────────────────────────────────┐
│ BIM Catalyst    Model    Analysis    Automation    AI       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│ ┌───────────────┐                       ┌─────────────────┐ │
│ │ AI Assistant  │                       │ Element         │ │
│ │               │       3D MODEL       │ Properties      │ │
│ │ Ask anything  │                       │                 │ │
│ │ about model   │                       │ Wall            │ │
│ │               │                       │ Level: 02       │ │
│ └───────────────┘                       │ Width: 200mm    │ │
│                                         └─────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### Custom graphics / overlays

This is especially relevant to your **AI/visualization work**.

You can add graphics on top of the Autodesk model without modifying the actual BIM geometry:

```text
          AI Analysis
              ↓
       ┌──────────────┐
       │     🔴       │
       │   MODEL      │
       │      🟡      │
       │         🟢   │
       └──────────────┘
          ↑    ↑
       Overlay graphics
```

Examples include:

* AI-generated markers
* Clash indicators
* Construction progress
* Heatmaps
* Risk visualization
* Carbon visualization
* Sensor locations
* IoT data
* Routing paths
* Design-analysis results
* Computational-design graphics
* Particle/flow visualizations
* Custom Three.js/WebGL graphics

### Model interaction

Your application can essentially treat the BIM model as a **queryable spatial database**.

For example:

```text
User clicks wall
       ↓
Viewer gives dbId
       ↓
Query model properties
       ↓
Category = Walls
Level = Level 02
FireRating = 2hr
Width = 200mm
       ↓
Your application
       ↓
AI / Rules / Analysis / Automation
```

You can therefore build workflows such as:

**Select → Analyze → Modify/Automate → Visualize result**

### Events

The Viewer exposes an event-driven architecture.

For example:

```text
MODEL_LOADED
     ↓
SELECTION_CHANGED
     ↓
CAMERA_CHANGED
     ↓
OBJECT_TREE_CREATED
     ↓
GEOMETRY_LOADED
     ↓
CUSTOM TOOL EVENT
```

Your application can subscribe to these events and react accordingly.

### Custom tools

You aren't restricted to Autodesk's interaction model.

You can create tools such as:

* Draw a line
* Draw a polygon
* Select an area
* Measure custom geometry
* Pick multiple elements
* Draw a path
* Create a spatial region
* Drag objects/markers
* Create custom BIM annotations
* Perform specialized engineering analysis

This is one of the capabilities that makes the Viewer suitable as the foundation for **engineering applications rather than simply a model viewer**.

---

## For a BIM Catalyst-style application

Given what you're building, I'd think of APS Viewer as:

> **A programmable BIM visualization engine that can become the 3D workspace of your SaaS application.**

You could build something like:

```text
                 BIM CATALYST
┌──────────────────────────────────────────────────────────────┐
│ Logo │ Models │ Analysis │ Automation │ AI │ Reports │ User │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  ┌─────────────┐                         ┌─────────────────┐ │
│  │ MODEL       │                         │ AI ASSISTANT    │ │
│  │             │                         │                 │ │
│  │ Architecture│                         │ "Analyze this   │ │
│  │ Structure   │        APS VIEWER       │  floor for      │ │
│  │ MEP         │                         │  clashes."      │ │
│  │             │                         │                 │ │
│  └─────────────┘                         └─────────────────┘ │
│                                                              │
│                  3D BIM / DIGITAL TWIN                      │
│                                                              │
├──────────────────────────────────────────────────────────────┤
│ Properties │ Analysis Results │ Automation │ Timeline        │
└──────────────────────────────────────────────────────────────┘
```

The **APS Viewer handles the BIM visualization and interaction**, while your application provides the intelligence and business logic.

This is particularly interesting for your planned **agentic BIM automation** because an AI agent could potentially operate at the level of:

**Natural language → identify model elements → analyze properties → invoke automation → visualize result → explain result**

rather than merely having a chatbot sitting beside a model.
