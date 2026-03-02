# Copilot Instructions

## General Guidelines
- User requests new code be covered with unit tests using xUnit + NSubstitute and prefers dependencies with permissive licenses for internal company use.
- User prioritizes implementing functionality and UX for the default screen over testing. 
- User prefers outputs in Czech and values visually appealing UI when implementing WPF screens.
- User explicitly does not want encryption for disk history archiving; the data is only an inventory state of the disk, and the workflow includes complete data deletion and the creation of an empty partition.
- User wants the ability to select/switch disks directly on each relevant page (e.g., Surface Test and SMART), independently of the default selection.

## UI Design Preferences
- User wants to debug the UI step-by-step in Czech, with better contrast and visual inspiration from the colors of Česká pošta; they also prefer functional feedback during the SMART quick test process.