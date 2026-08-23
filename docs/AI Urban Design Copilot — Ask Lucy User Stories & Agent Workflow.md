# AI Urban Design Copilot
## Ask Lucy — User Stories & Conversational Agent Workflow

**Document Type:** Product / Agent Behavior Specification  
**Application:** AI Urban Design Copilot  
**Primary AI Agent:** Ask Lucy  
**Purpose:** Convert the existing Urban Design mockup into a functional conversational application.

---

# 1. Objective

Transform the existing AI Urban Design Copilot mockup into a functional application in which **Ask Lucy** is the primary conversational interface between the user and the Urban Design system.

Ask Lucy must:

1. Understand the user's natural-language project description.
2. Determine whether the user is referring to a physical asset, a digital project, or both.
3. Search for existing projects and assets.
4. Create a digital project when required.
5. Gather missing project requirements through conversation.
6. Collect or request site information.
7. Use available project, GIS, map, document, and design skills.
8. Analyze the existing urban context.
9. Generate multiple design alternatives.
10. Explain the reasoning behind each alternative.
11. Compare alternatives using measurable KPIs.
12. Accept iterative user feedback.
13. Create new design versions.
14. Recommend an optimal solution.
15. Maintain a complete conversational and design history.

Ask Lucy is the **orchestration and reasoning layer**.

Ask Lucy does NOT directly implement backend operations.

Backend operations are executed through Skills and Services.

---

# 2. Core Architecture

The functional architecture should follow:

```text
                    USER
                      │
                      ▼
                 ASK LUCY
             Conversation Agent
                      │
                      ▼
               Intent / Reasoning
                      │
                      ▼
              Skill Orchestrator
                      │
       ┌──────────────┼──────────────┐
       ▼              ▼              ▼
 Project Skills    GIS Skills    Design Skills
       │              │              │
       ▼              ▼              ▼
 Project API       GIS APIs      Design Engine
       │
       ▼
 Existing Project Platform
```

Ask Lucy should never directly call external APIs.

Instead:

```text
Ask Lucy
    ↓
Skill
    ↓
Service
    ↓
External/Internal API
```

---

# 3. Important Domain Model

The system must distinguish between three concepts.

## 3.1 Physical Asset

The real-world object.

Examples:

- Al Safa Park
- Existing building
- Existing road
- Existing plaza

Represented conceptually as:

```text
Physical Asset
```

---

## 3.2 Digital Project

The project representation stored in the application's database/project platform.

Examples:

```text
Al Safa Park Urban Enhancement
```

---

## 3.3 Digital Representation

Any digital representation associated with the physical asset or project.

Examples:

- GIS
- CAD
- Revit
- IFC
- 2D drawings
- 3D models
- Documents
- Images
- Digital Twin
- APS Viewer model

These concepts must not be treated as synonyms.

---

# 4. User Story 01 — Start Urban Design Session

## User Story

**As a user**, I want to describe my urban design objective naturally so that Ask Lucy can understand what I want to accomplish without requiring me to fill out a complex form.

## Example

User:

> I want to redesign Al Safa Park in Dubai.

Ask Lucy should acknowledge the intent and begin identifying the project.

Example:

> I can help you redesign Al Safa Park. Before we begin, I’ll check whether the park already exists as a physical asset and whether there is an existing digital project in the system.

Ask Lucy should then initiate project/asset discovery.

---

# 5. User Story 02 — Identify Physical Asset

## User Story

**As a user**, I want Ask Lucy to determine whether the project refers to a real-world existing site.

## Example

User:

> The park already exists in Dubai.

Ask Lucy records:

```json
{
  "physicalAssetExists": true,
  "assetType": "Park",
  "assetName": "Al Safa Park",
  "location": "Dubai"
}
```

If the location is incomplete, Ask Lucy asks for clarification.

Example:

> I found several locations with similar names. Can you confirm that you mean Al Safa Park in Dubai?

---

# 6. User Story 03 — Search Existing Digital Project

Once the physical asset is identified, Ask Lucy must check whether a corresponding digital project exists.

Invoke:

```text
SearchProjectSkill
```

The skill should search the existing project platform.

Possible results:

```text
FOUND
```

or

```text
NOT_FOUND
```

or

```text
MULTIPLE_MATCHES
```

---

# 7. User Story 04 — Existing Physical Asset, No Digital Project

## Scenario

```text
Physical Asset = EXISTS
Digital Project = DOES NOT EXIST
```

Example:

> Al Safa Park physically exists, but there is no project for it in the system.

Ask Lucy should explain:

> I found the physical park, but I couldn't find an existing digital project associated with it.

Then ask:

> Would you like me to create a digital project for this existing park?

Options:

```text
Create Project
Use Different Project
Cancel
```

Ask Lucy must wait for confirmation before creating the project.

---

# 8. User Story 05 — Create Digital Project

After user confirmation:

Invoke:

```text
CreateProjectSkill
```

Suggested initial project name:

```text
Al Safa Park Urban Enhancement
```

Ask Lucy may suggest the name instead of immediately asking the user to type one.

Example:

> I suggest naming the project "Al Safa Park Urban Enhancement". Would you like to use this name?

User:

> Yes.

Then execute:

```text
CreateProjectSkill
```

The skill should return:

```json
{
  "success": true,
  "projectId": "...",
  "projectName": "Al Safa Park Urban Enhancement"
}
```

Ask Lucy may only tell the user that the project was created after receiving successful execution results.

---

# 9. User Story 06 — Existing Physical Asset AND Existing Digital Project

## Scenario

```text
Physical Asset = EXISTS
Digital Project = EXISTS
```

Ask Lucy should not create another project.

Instead:

> I found an existing digital project associated with Al Safa Park. Would you like to continue working with this project?

Options:

```text
Open Existing Project
Create Separate Project
Cancel
```

If the user selects the existing project:

Invoke:

```text
OpenProjectSkill
```

Then load:

- project metadata
- documents
- models
- previous design versions
- GIS references
- previous analysis
- user decisions

---

# 10. User Story 07 — Digital Project Exists But Physical Asset Is Not Built

## Scenario

```text
Physical Asset = DOES NOT EXIST
Digital Project = EXISTS
```

This represents a project that is:

- conceptual
- under design
- under construction
- planned
- not yet completed

Ask Lucy should understand that this is not an existing physical asset.

Example:

> I found a digital project for this site, but there is no confirmed existing physical asset. I’ll treat the project as a proposed or planned development unless you tell me otherwise.

Ask Lucy should then continue using the existing digital project.

---

# 11. User Story 08 — Neither Physical Asset nor Digital Project Exists

## Scenario

```text
Physical Asset = DOES NOT EXIST
Digital Project = DOES NOT EXIST
```

Ask Lucy should treat this as a new project.

Example:

> I couldn't find an existing physical asset or digital project. I'll treat this as a new urban design project.

Then collect the minimum information required to create the project.

---

# 12. User Story 09 — Gather Urban Design Requirements

Once the project is identified, Ask Lucy should progressively gather requirements.

Do NOT display a large form.

Ask one logical question at a time.

Potential requirement categories:

### Project Objective

Examples:

- Redesign
- Revitalization
- Accessibility improvement
- Smart city transformation
- Sustainability
- Landscape enhancement
- Mobility improvement

### Target Users

Examples:

- Families
- Children
- Elderly
- People with disabilities
- Tourists
- Athletes
- Local residents

### Design Priorities

Examples:

- Accessibility
- Sustainability
- Smart infrastructure
- Safety
- Walkability
- Biodiversity
- Community activities
- Sports
- Tourism

### Budget

Examples:

- Low
- Medium
- High
- Unknown

### Existing Elements

Examples:

- Preserve major trees
- Preserve existing paths
- Preserve existing playgrounds
- Preserve structures
- Preserve parking

### Technology

Examples:

- Wi-Fi
- Smart lighting
- IoT sensors
- Digital signage
- Smart irrigation
- Visitor counting

---

# 13. User Story 10 — Avoid Asking Known Information

Ask Lucy must maintain a structured requirement state.

Example:

```json
{
  "projectType": "Urban Park",
  "location": "Dubai",
  "physicalAssetExists": true,
  "digitalProjectExists": true,
  "primaryGoals": [
    "Accessibility",
    "Sustainability",
    "Smart City"
  ]
}
```

If information is already known, Ask Lucy must not ask the same question again.

Instead it should identify the next missing high-value requirement.

---

# 14. User Story 11 — Determine Requirement Completeness

Ask Lucy should continuously evaluate:

```text
Known Information
+
Missing Information
+
Required Information
```

Example:

```text
Project Type              ✓
Location                  ✓
Existing Asset            ✓
Project                    ✓
Primary Goals             ✓
Target Users              ✓
Budget                    ✓
Existing Landscape        ✓
GIS Context               pending
Design Constraints        pending
```

Ask Lucy should ask only for information that is necessary for the next stage.

---

# 15. User Story 12 — Site Intelligence Collection

Once enough information is available, Ask Lucy should initiate site analysis.

Invoke relevant skills:

```text
SearchLocationSkill
GetSiteBoundarySkill
GetGISDataSkill
GetMapDataSkill
GetTransportationDataSkill
GetSatelliteDataSkill
GetClimateDataSkill
```

Skills may be mocked during the first implementation if live integrations are not ready.

The architecture must nevertheless treat them as real Skills.

---

# 16. User Story 13 — Build Site Context

The system should create a structured site context.

Example:

```json
{
  "site": {
    "name": "Al Safa Park",
    "city": "Dubai",
    "area": "...",
    "boundary": "..."
  },
  "context": {
    "roads": [],
    "buildings": [],
    "transportation": [],
    "walkways": [],
    "greenAreas": [],
    "amenities": [],
    "pointsOfInterest": [],
    "climate": {}
  }
}
```

---

# 17. User Story 14 — Site Analysis

Invoke:

```text
AnalyzeSiteSkill
```

The analysis should evaluate:

### Accessibility

- sidewalk connectivity
- pedestrian access
- barrier-free routes
- entrance accessibility
- proximity to transport

### Mobility

- roads
- public transport
- pedestrian flow
- cycling
- parking

### Environment

- vegetation
- shade
- heat exposure
- water
- biodiversity

### Urban Context

- surrounding buildings
- land use
- population/activity context
- nearby destinations

### Services

- schools
- retail
- restaurants
- healthcare
- community facilities

### Smart City

- connectivity opportunities
- IoT opportunities
- smart lighting
- smart irrigation
- digital services

---

# 18. User Story 15 — Explain Evidence

Every important AI insight should have:

```text
Finding
Evidence
Source
Reasoning
Confidence
```

Example:

```text
Finding:
Poor pedestrian connectivity on the western edge.

Evidence:
Two pedestrian paths terminate before reaching the main entrance.

Source:
GIS / OpenStreetMap analysis.

Reasoning:
Disconnected paths reduce walkability and accessibility.

Confidence:
91%
```

Ask Lucy should be able to explain findings conversationally.

---

# 19. User Story 16 — Generate Design Alternatives

After site analysis:

Invoke:

```text
GenerateDesignOptionsSkill
```

The system should generate at least three alternatives.

Example:

```text
Option A
Accessibility First

Option B
Smart Park

Option C
Eco Park
```

Then generate a fourth hybrid option:

```text
Option D
AI Hybrid Optimization
```

The hybrid option should combine compatible strengths from the previous alternatives.

---

# 20. User Story 17 — Design Option Structure

Each option must contain:

```json
{
  "id": "option-d",
  "name": "AI Hybrid Optimization",
  "description": "...",
  "designPrinciples": [],
  "interventions": [],
  "kpis": {},
  "pros": [],
  "cons": [],
  "risks": [],
  "assumptions": []
}
```

---

# 21. User Story 18 — Generate Design Insights

For every design intervention, Ask Lucy should explain:

```text
What changed?
Why was it changed?
What evidence supports the change?
What KPI does it improve?
What trade-off does it introduce?
```

Example:

> I moved the main seating zone toward the eastern side because the western edge has higher heat exposure. This improves thermal comfort while preserving the western area for shade-producing landscape interventions.

---

# 22. User Story 19 — Evaluate Design Alternatives

Each option must be evaluated using the same KPI framework.

Initial KPI set:

```text
Accessibility
Sustainability
Smart City Readiness
Walkability
Safety
Visitor Experience
Biodiversity
Maintenance
Cost
Mobility
```

Scores should use a consistent 0–100 scale.

---

# 23. User Story 20 — Weighted Scoring

The system must support user-defined priorities.

Example:

```text
Accessibility          25%
Sustainability         20%
Smart City             15%
Visitor Experience     15%
Safety                 10%
Mobility               10%
Cost                    5%
```

Overall score:

```text
Overall Score =
Σ(KPI Score × KPI Weight)
```

Weights must always be normalized to 100%.

Ask Lucy should explain that changing priorities can change the recommended design.

---

# 24. User Story 21 — Compare Options

Display:

- KPI comparison
- radar chart
- strengths
- weaknesses
- cost
- implementation complexity
- risks
- expected impact

Example:

| KPI | A | B | C | D |
|---|---:|---:|---:|---:|
| Accessibility | 98 | 84 | 88 | 96 |
| Sustainability | 80 | 82 | 99 | 95 |
| Smart City | 70 | 98 | 72 | 94 |
| Safety | 90 | 88 | 86 | 92 |
| Visitor Experience | 88 | 93 | 90 | 97 |
| Overall | 88 | 90 | 91 | **97** |

---

# 25. User Story 22 — Explain Pros and Cons

Ask Lucy should explicitly communicate trade-offs.

Example:

### Option A

Pros:

- highest accessibility
- excellent inclusive design
- simple technology requirements

Cons:

- lower smart city score
- fewer digital services

### Option B

Pros:

- highest technology readiness
- excellent connectivity
- smart lighting and IoT

Cons:

- higher implementation cost
- higher maintenance requirements

### Option C

Pros:

- highest ecological performance
- lower water consumption
- stronger biodiversity

Cons:

- fewer smart services

---

# 26. User Story 23 — Recommend Optimal Option

Ask Lucy should not simply select the highest numerical score.

It must consider:

```text
User priorities
+
Site constraints
+
KPI scores
+
Cost
+
Feasibility
+
Trade-offs
```

Example:

> Based on your priorities, site conditions and the weighted evaluation, I recommend Option D.

Then explain:

> Option D does not achieve the absolute highest score in every individual category, but it provides the best overall balance between accessibility, sustainability, smart infrastructure, visitor experience and implementation cost.

---

# 27. User Story 24 — User Requests Modification

The user can continue naturally.

Example:

> I like Option D, but I want to add a children's water play area.

Ask Lucy should understand this as a design modification.

It should NOT restart the project.

It should:

1. Identify the current design version.
2. Interpret the requested modification.
3. Analyze its impact.
4. Update the design.
5. Create a new version.

---

# 28. User Story 25 — Design Versioning

Example:

```text
Version 1
Initial Hybrid Concept

Version 2
Added Children's Water Play Area

Version 3
Reduced Water Consumption

Version 4
Final Optimized Design
```

Each version must maintain:

- changes
- reason
- user request
- affected KPIs
- timestamp
- parent version

---

# 29. User Story 26 — Impact Analysis After Modification

After a change, Ask Lucy should explain its impact.

Example:

> Adding the water play area improves family experience by 8 points, but increases water demand and maintenance requirements.

Then show:

```text
Visitor Experience   +8
Family Engagement    +10
Water Efficiency     -4
Maintenance          -3
Cost                 +5%
```

---

# 30. User Story 27 — Final Optimization

The user may say:

> Optimize it and give me the best final version.

Ask Lucy should:

1. Analyze all existing options.
2. Analyze the current version.
3. Review user priorities.
4. Identify compatible improvements.
5. Remove unnecessary interventions.
6. Generate the final optimized concept.
7. Recalculate KPIs.

---

# 31. User Story 28 — Final Approval

Ask Lucy should present:

```text
Recommended Final Design

Al Safa Park
Urban Smart Park Enhancement

Overall Score: 97/100
```

Then:

```text
Key Benefits

✓ Accessibility
✓ Sustainability
✓ Smart Infrastructure
✓ Walkability
✓ Visitor Experience
✓ Climate Response
```

Ask:

> Would you like to approve this as the final concept?

Only after confirmation should the design be marked:

```text
FINAL_APPROVED
```

---

# 32. User Story 29 — Generate Deliverables

After approval:

Invoke:

```text
GenerateReportSkill
GenerateGISPackageSkill
GeneratePresentationSkill
Generate3DPreviewSkill
```

The system should prepare:

- Executive Summary
- Site Analysis
- Design Rationale
- Design Comparison
- KPI Analysis
- Final Recommendation
- GIS package
- 2D concept
- 3D preview

---

# 33. Ask Lucy Conversation Rules

Ask Lucy must follow these rules.

## Rule 1 — Never fabricate execution

Never say:

> Project created.

unless the Project Creation Skill returned success.

Never say:

> GIS data loaded.

unless the GIS Skill returned data.

Never say:

> Design generated.

unless the design generation process returned a valid result.

---

## Rule 2 — Ask only necessary questions

Avoid interrogating the user.

Prefer:

> What is your primary goal for the redesign?

instead of showing 20 questions.

---

## Rule 3 — Remember previous answers

Do not repeatedly ask for information already known.

---

## Rule 4 — Explain why questions matter

Example:

> Do you want to preserve the major existing trees? This will affect the available design area and our sustainability evaluation.

---

## Rule 5 — Ask confirmation before consequential actions

Examples:

- Creating a project
- Deleting a project
- Overwriting an existing design
- Finalizing a design

---

## Rule 6 — Prefer recommendations over forms

Instead of:

> Enter project name.

Ask:

> I suggest "Al Safa Park Urban Enhancement". Would you like to use that name?

---

## Rule 7 — Separate facts from AI recommendations

The UI must distinguish:

```text
Observed Data
```

from:

```text
AI Interpretation
```

and:

```text
AI Recommendation
```

---

# 34. Skill Architecture

Initial Skills:

```text
PROJECT

SearchProjectSkill
CreateProjectSkill
OpenProjectSkill
UpdateProjectSkill
UploadDocumentSkill


LOCATION / GIS

SearchLocationSkill
GetSiteBoundarySkill
GetGISDataSkill
GetMapDataSkill
GetTransportationDataSkill
GetSatelliteDataSkill


ANALYSIS

AnalyzeSiteSkill
AnalyzeAccessibilitySkill
AnalyzeMobilitySkill
AnalyzeSustainabilitySkill
AnalyzeSmartCitySkill


DESIGN

GenerateDesignOptionsSkill
EvaluateDesignSkill
CompareDesignOptionsSkill
ModifyDesignSkill
CreateDesignVersionSkill
RecommendFinalDesignSkill


OUTPUT

GenerateReportSkill
GenerateGISPackageSkill
Generate3DPreviewSkill
```

---

# 35. Skill Execution Architecture

The implementation should follow:

```text
Ask Lucy
    ↓
Intent / Reasoning
    ↓
Skill Selection
    ↓
Skill Validation
    ↓
Skill Execution
    ↓
Service
    ↓
API / Backend
    ↓
Skill Result
    ↓
Ask Lucy
    ↓
User
```

Ask Lucy must not directly call application APIs.

---

# 36. Skill Result Contract

All Skills should return a consistent structure.

Example:

```json
{
  "success": true,
  "skill": "CreateProjectSkill",
  "data": {},
  "message": "Project created successfully",
  "errors": [],
  "metadata": {}
}
```

Failure:

```json
{
  "success": false,
  "skill": "CreateProjectSkill",
  "data": null,
  "message": "Project could not be created",
  "errors": [
    "Project name already exists"
  ]
}
```

Ask Lucy must interpret these results conversationally.

---

# 37. Conversation State

Maintain a structured conversation state.

Example:

```json
{
  "sessionId": "...",
  "asset": {
    "name": "Al Safa Park",
    "type": "Park",
    "physicalExists": true,
    "location": "Dubai"
  },
  "project": {
    "exists": true,
    "projectId": "...",
    "projectName": "Al Safa Park Urban Enhancement"
  },
  "requirements": {
    "goals": [
      "Accessibility",
      "Sustainability",
      "Smart City"
    ],
    "budget": "Medium",
    "preserveExistingTrees": true,
    "iot": true
  },
  "analysis": {},
  "designOptions": [],
  "currentVersion": "v1",
  "approvedVersion": null
}
```

This state should be persisted.

Do not rely on the LLM's conversation context alone.

---

# 38. Demo Scenario

The first fully implemented end-to-end scenario must support:

### Step 1

User:

> I want to redesign Al Safa Park in Dubai.

### Step 2

Ask Lucy identifies:

```text
Physical Asset = EXISTS
```

### Step 3

Ask Lucy searches:

```text
Digital Project = NOT FOUND
```

### Step 4

Ask Lucy recommends creating:

```text
Al Safa Park Urban Enhancement
```

### Step 5

User confirms.

### Step 6

Project is created through the Project Skill.

### Step 7

Ask Lucy gathers:

```text
Accessibility
Sustainability
Smart City
Medium Budget
Preserve Major Trees
IoT = Yes
```

### Step 8

GIS/site data is gathered.

### Step 9

Site analysis is displayed.

### Step 10

Three design alternatives are generated.

### Step 11

Hybrid Option D is generated.

### Step 12

Options are evaluated.

### Step 13

Ask Lucy explains pros/cons and trade-offs.

### Step 14

User selects Option D.

### Step 15

User says:

> Add a children's water play area.

### Step 16

Ask Lucy creates Version 2.

### Step 17

Impact analysis is displayed.

### Step 18

User says:

> Optimize the final design.

### Step 19

Ask Lucy generates final optimized design.

### Step 20

User approves.

### Step 21

Final project state becomes:

```text
FINAL_APPROVED
```

---

# 39. UI Requirements

The UI should have four major areas.

## 1. Ask Lucy Panel

Conversation.

## 2. Context Panel

Map / GIS / Site.

## 3. Analysis Panel

Charts / KPIs / Insights.

## 4. Design Panel

2D / 3D / Options / Versions.

The active panel should change automatically based on the current conversation stage.

---

# 40. AI Transparency

The user should always understand what Ask Lucy is doing.

Examples:

```text
Understanding your request...
```

```text
Checking existing projects...
```

```text
Analyzing site context...
```

```text
Evaluating accessibility...
```

```text
Generating design alternatives...
```

```text
Comparing options...
```

Avoid exposing private chain-of-thought.

Only display concise action/status summaries and evidence-based explanations.

---

# 41. Important MVP Constraint

Do not attempt to build the entire future Engineering Intelligence Platform now.

Implement only the capabilities required for this Urban Design scenario.

However, architecture must remain extensible.

The following future capabilities should be possible without redesigning Ask Lucy:

```text
Building Design
BIM
Construction
Infrastructure
Landscape
Digital Twin
Facility Management
Cost
Schedule
Risk
Compliance
```

The current implementation should therefore treat:

```text
Project
Asset
Document
Model
GIS
Design
Analysis
Version
Skill
```

as reusable platform concepts.

---

# 42. Definition of Done

The feature is complete when a user can start with:

> I want to redesign Al Safa Park in Dubai.

and Ask Lucy can guide the user through the complete journey:

```text
Conversation
    ↓
Asset Identification
    ↓
Project Discovery
    ↓
Project Creation / Opening
    ↓
Requirement Gathering
    ↓
Site Intelligence
    ↓
Site Analysis
    ↓
Design Alternatives
    ↓
KPI Evaluation
    ↓
Comparison
    ↓
Design Modification
    ↓
Versioning
    ↓
Optimization
    ↓
Final Approval
    ↓
Deliverables
```

The user should experience this as **one continuous conversation with Ask Lucy**, while the underlying application orchestrates the required Skills, APIs, data sources, analysis engines, and visualization components.

The implementation must preserve the distinction between:

**what the user said**

**what the system knows**

**what external data confirms**

**what the AI recommends**

**what the user approved**

This distinction is fundamental to the credibility of the AI Urban Design Copilot.