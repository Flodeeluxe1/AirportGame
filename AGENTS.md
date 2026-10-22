# Unity 6 Airport Game — Project Instructions

## Project Overview

We are developing a Unity 6 URP airport management game.

- **GitHub repository:** https://github.com/Flodeeluxe1/AirportGame
- **Branch:** `main`
- **Engine:** Unity 6 with URP
- **Workflow:** I apply scripts manually in Unity and update the GitHub repository at regular intervals.

These instructions apply to all script creation, code changes, debugging, and implementation suggestions for this project.

## 1. Inspect Existing Code First

- Inspect the actual repository scripts, dependencies, classes, and existing methods before proposing changes.
- Do not guess how the project works or assume that a method, field, or component exists.
- Check how the affected script interacts with other controllers and systems.
- If repository access is unavailable, or the relevant local version may differ from GitHub, ask me to provide the current script when necessary.
- Never replace a script based on an outdated version without considering the possibility of newer local changes.

## 2. Choose the Appropriate Code Change

Use the following rules to decide how to deliver code.

### Create a new script
Create a new `.cs` file when a genuinely new component or separate responsibility is required.

Specify its exact file path, class name, purpose, and any required setup.

### Replace an existing function
Provide a complete function replacement when the change is self-contained and limited to one method.

Identify the exact script path and method name. Explain what to select and replace, and whether any additional helper methods or changes are needed.

### Replace an entire script
Prefer a complete script replacement when:
- Multiple functions need coordinated changes.
- More than two lines need changing in different locations.
- A partial edit would be confusing, error-prone, or difficult to apply safely.

Before supplying a full replacement, inspect the current script and preserve all unrelated functionality.

### Add a small piece of code
For a genuinely small, localized change, provide the exact insertion point and the code to add. State clearly which existing code must remain unchanged.

**Default principle:** Choose the smallest safe change that solves the problem, unless a complete script replacement would be clearer and safer.

## 3. Preserve Working Features

- Do not perform unrelated refactoring or remove working functionality unnecessarily.
- Preserve existing behavior, public APIs, serialized Inspector fields, component references, state transitions, debugging output, and inter-script dependencies.
- Consider the effects of changes on connected controllers and gameplay systems.
- The confirmed takeoff sequence is a protected working system. Do not change it unless we explicitly agree to do so.
- Pay particular attention to interactions between airport operations, aircraft movement, traffic, intersections, parking, cargo zones, and other connected systems where relevant.
- If a proposed change could break an existing system, explain the risk and ask before proceeding when the intended behavior or acceptable trade-off is unclear.

## 4. Give Precise Implementation Instructions

For every code change, clearly state:

1. The exact script path.
2. Whether to create a new script, add code, replace a function, or replace an entire script.
3. The complete code to use.
4. Which existing code to remove or retain.
5. Any helper methods, fields, enums, or other dependent changes required.
6. Whether other scripts also need changes.

Avoid vague instructions such as "update the movement code" without identifying the exact location.

Do not leave out necessary dependencies when providing a replacement.

## 5. Explain Unity Editor Setup

Whenever a change requires Unity Editor setup, explain it step by step.

This includes:
- Inspector references and serialized fields.
- Adding components to GameObjects.
- Layers, tags, and collision settings.
- Scene configuration.
- Script references and component dependencies.
- Runtime initialization and testing steps.

Do not assume that a newly created script automatically has its required references or setup.

## 6. GitHub and Repository Workflow

The GitHub repository is our reference point, but it may not always contain my latest local changes.

- I update the repository periodically and will tell you when an update has been made.
- I may modify scripts locally before updating GitHub.
- Keep track within the available conversation context of which scripts we have changed and whether those changes may still be local-only.
- When I tell you that I updated the repository, use that as a checkpoint and inspect the updated version before making further changes.
- Before replacing an existing script, check whether GitHub may contain an older version than the one currently in Unity.
- If there is a version discrepancy, ask me for the latest local script rather than silently overwriting newer work.

### Permission requirements

- Do not modify GitHub or push commits without my explicit permission.
- Do not assume permission to commit, push, merge, or otherwise change the repository.
- I apply scripts manually and manage repository updates myself.

When proposing changes, identify exactly which files I need to add or update.

## 7. Do Not Modify Prefabs

Do not modify prefabs.

If a proposed implementation appears to require a prefab change, stop and explain the requirement. Ask me before proceeding with an alternative or requesting an exception.

Prefer solutions that work through scripts or permitted scene and Inspector setup without modifying prefabs.

## 8. Be Honest About Testing

- Never claim that code has been tested in Unity unless it actually has been tested there.
- Never claim that the project compiles, a feature works, or a bug is fixed without appropriate verification.
- Never claim that changes have been committed or pushed unless that actually happened.
- Clearly distinguish between code inspection, reasoned expectations, and actual test results.
- When testing is not possible, provide practical steps for me to verify the change in Unity.

## 9. Ask Rather Than Guess

If the requirements are ambiguous or a change could affect existing working systems, ask me before making assumptions.

Do not invent missing project architecture, intended behavior, script contents, or Inspector configuration.

If there are multiple plausible implementations, explain the relevant differences and ask a focused question when the decision matters.

## 10. Recommend Better Approaches Honestly

If there is a safer, simpler, or more maintainable approach than the one initially proposed, explain it.

Describe:
- Why you recommend it.
- Its advantages and disadvantages.
- Any risks or effects on existing systems.

Be candid about technical concerns. I make the final decision about which approach to use.

## 11. No ZIP Downloads

- Do not offer ZIP files for download.
- Provide scripts directly in the conversation as copy-and-paste code.
- Clearly identify the filename and exact path for every new or modified script.
- When a complete script replacement is needed, provide the entire script rather than an incomplete excerpt.

## 12. Standard Workflow for Every Change

Follow this sequence:

1. **Inspect** the current relevant code and dependencies.
2. **Establish the baseline** by identifying the latest known version and whether local changes may be missing from GitHub.
3. **Plan** the smallest safe implementation.
4. **Protect existing systems** and identify any risks or unresolved decisions.
5. **Implement** using the appropriate new-script, function-replacement, addition, or full-script-replacement approach.
6. **Explain setup** and any dependent script or Inspector changes.
7. **Verify what can be verified** and state what still needs testing in Unity.
8. **List the exact files** I need to add or update.

### Final guiding principle

**Inspect first. Never guess. Preserve working functionality. Make precise, minimal, safe changes. Explain the implementation clearly. Ask when uncertain. Never change GitHub or prefabs without explicit permission.**