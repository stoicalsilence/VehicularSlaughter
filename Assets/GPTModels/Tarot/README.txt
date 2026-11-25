GEOMETRIC TAROT STARTER PACK
============================
Nine front designs based on the minimalist card wireframes, one blank template,
and one shared card back. Original geometric vector paths; no generated raster
illustrations or third-party icon artwork used. Text uses Liberation Serif (or
serif fallback). Font files are not included.

models/
  Individual .glb cards with embedded front/back PNGs and three material slots.
  blank.glb is the reusable card model with empty front template.
  card.obj + card.mtl are an alternative shared mesh with relative texture paths.
  Keep the folder structure intact if using OBJ.

textures/
  900 x 1450 RGBA PNGs, exact 18:29 aspect ratio.
svg/
  Editable SVG versions; replace the group id="symbol" and title/number text.
  In Inkscape, install/select Liberation Serif or choose a similar serif font.

MESH
  Width 0.18 m, height 0.29 m, thickness 0.004 m.
  Rounded corners, beveled face edges, 160 triangles, centered origin.
  Y is up, +Z is front in glTF; back texture UVs face the reader correctly.
  Three materials: Card Front, Card Back, Card Edge.
  Front/back GLB materials use KHR_materials_unlit; edge uses ordinary PBR.
  If your importer ignores unlit, set the front/back material to unlit manually.
  Apply your own scaling and rotation to fit your UI scene and engine conventions.

SUGGESTED USE
  Import one GLB first, check card orientation, scale and readability.
  For the final system, reuse blank.glb, instantiate three copies, and assign
  different front textures per instance without modifying the shared material.
  Rotate about local Y to flip; pivot is centered. Keep rules descriptions,
  durations and effect values as live UI text, not baked into the textures.
  Import settings and animation are engine-specific and are not included.
  These assets have been checked structurally, but not tested in your game.
