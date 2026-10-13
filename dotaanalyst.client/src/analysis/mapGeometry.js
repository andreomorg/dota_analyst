// Map units of the replay: Radiant's base bottom-left. The maps' SVG viewBox uses the same units, with y flipped
// (SVG y grows downwards; the map's grows upwards).

// The minimap image (GameMap.jsx), as the game places it (resource/overviews/dota.txt: pos_x -9472, pos_y 9472,
// scale 18.5 units per pixel of a 1024 px image): it covers -9472..9472 on both axes.
export const MINIMAP_ORIGIN = -9472;
export const MINIMAP_SIZE = 18944;

// What the maps show: the playable area, without most of the image's dark frame.
export const EXTENT = 8450;
export const VIEW_BOX = `${-EXTENT} ${-EXTENT} ${2 * EXTENT} ${2 * EXTENT}`;

export const svgY = y => -y;
