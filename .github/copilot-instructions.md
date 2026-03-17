# Copilot Instructions

## General Guidelines
- User requests new code be covered with unit tests using xUnit + NSubstitute and prefers dependencies with permissive licenses for internal company use.
- User prioritizes implementing functionality and UX for the default screen over testing. 
- User prefers outputs in Czech and values visually appealing UI when implementing WPF screens.
- User explicitly does not want encryption for disk history archiving; the data is only an inventory state of the disk, and the workflow includes complete data deletion and the creation of an empty partition.
- User wants the ability to select/switch disks directly on each relevant page (e.g., Surface Test and SMART), independently of the default selection.
- User wants to clean up the structure of the WPF project first: each class in its own file, remove outdated and redundant files (including unnecessary .md files), and only then address further UX improvements (graphs/gauges).
- User wants a visually attractive certificate (colorful and suitable for black-and-white printing), featuring a graph and a prominent large seal; the next step should focus on usability and final UX of the Avalonia project.

## UI Design Preferences
- User wants to debug the UI step-by-step in Czech, with better contrast and visual inspiration from the colors of Česká pošta; they also prefer functional feedback during the SMART quick test process.
- User prefers in the Surface Test to have overlay graphs for write and read operations in the range of 0–100%, with corresponding GB values displayed alongside the percentage on the X-axis in a single-line format: "<percentage>% - <capacity>", rather than a line break.
- User prefers intuitive colors for the legend in the Surface Test graph: label "Zápis" in red and label "Čtení" in green.