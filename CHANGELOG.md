-- 2025-11-29 - v1.0.1

- refactor: replace manual distance-based sound system with entity-based 3D audio
- refactor: replace custom vector distance calculation with built-in Length() method
- fix: play sounds when action starts instead of when it completes
- fix: replace incorrect sound for hostage drop action
- chore: remove unused code (ActionType.None, ActionState.StartTime, CalculateDistance)
