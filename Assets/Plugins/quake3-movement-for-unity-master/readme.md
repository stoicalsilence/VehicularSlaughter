Implementation of Quake III "vanilla" and Challenge ProMode Arena (CPMA) strafe jumping mechanics in the Unity engine.

This is my own updated and enhanced fork of the original scripts created by [WiggleWizard](https://github.com/WiggleWizard). So most of the credit goes to him for porting it over. I am just trying to improve upon it some here.

## Notes:

### Coordinate System
Quake uses a right-handed coordinate system while Unity uses a left-handed one. So coordinate values (X,Y,Z) have been swapped to reflect this difference.

### World Scale
UPS (units per second) is measured in Unity units (meters) and not idTech units.

### Configuration
Default script values emulate Quake III Arena movement with CPM(A) physics.

### Demo Assets
Demo scene meshes were built with ProBuilder 4.4 so this package must be installed in your project for demo scene to function.
