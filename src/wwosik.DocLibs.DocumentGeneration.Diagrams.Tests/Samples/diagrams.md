# Diagrams

Every Mermaid diagram type the renderer draws, in the brand's colours and font.

## Flowchart

```mermaid
flowchart TD
  A[Markdown document] --> B{Has front matter?}
  B -->|yes| C[Apply the brand]
  B -.->|no| D[(Neutral brand)]
  C ==> E([Lay out pages])
  D --> E
  subgraph Output
    E --> F>PDF/A-2b]
    E --> G{{PowerPoint deck}}
  end
  G --- H((Done))
```

## Sequence

```mermaid
sequenceDiagram
  participant App
  actor User
  App->>User: Asks for the report
  User-->>App: Sends the Markdown
  Note over App,User: The brand comes from front matter
  loop Every chapter
    App-)User: Progress
  end
  App-xUser: Done
```

## Class

```mermaid
classDiagram
  class Brand {
    +String Name
    +BrandColors Colors
    +Resolve() ResolvedBrand
  }
  class BrandColors
  Brand *-- BrandColors
  Brand o-- BrandFonts : fonts
  Brand ..> PptxBrandTemplate
  ICodeBlockRenderer <|.. MermaidRenderer
```

## State

```mermaid
stateDiagram-v2
  [*] --> Draft
  Draft --> Review : submit
  Review --> Draft : changes
  Review --> Published : approve
  state Published {
    [*] --> Live
    Live --> Archived
  }
  Published --> [*]
```

## Entity relationship

```mermaid
erDiagram
  CUSTOMER ||--o{ ORDER : places
  ORDER ||--|{ LINE_ITEM : contains
  CUSTOMER {
    string name PK
    string email
  }
```

## Pie

```mermaid
pie title Revenue by region
  "North" : 1200
  "South" : 950
  "East" : 610
  "West" : 430
```

## Gantt

```mermaid
gantt
  title Release plan
  dateFormat YYYY-MM-DD
  section Build
  Design :a1, 2026-01-05, 10d
  Implement :after a1, 20d
  section Ship
  Verify :2026-02-05, 7d
  Release :milestone, 2026-02-16, 0d
```

## Mindmap

```mermaid
mindmap
  root((DocLibs))
    Conversions
      PDF
      DOCX
      PPTX
    Generation
      PDF/A
      Decks
      [Brands]
```

## Not drawn yet

A journey stays code:

```mermaid
journey
  title Writing a report
  section Draft
    Write: 4: Author
```
