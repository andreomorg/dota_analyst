// A schematic Dota map of our own (lanes, river, bases — not Valve's art), shared by the death and ward maps.
// Drawn in map units (mapGeometry.js), so it scales to any width without stretching.

import { EXTENT, svgY } from './mapGeometry';

const toSvg = ([x, y]) => `${x},${svgY(y)}`;
const path = points => `M${points.map(toSvg).join('L')}`;

// Approximate lanes from the towers' positions (the same ones the Go parser's zones use).
const LANES = [
    [[-6900, -4300], [-6450, 5950], [4000, 6400]], // top
    [[-4500, -4000], [4000, 3600]], // middle
    [[-4300, -6800], [6350, -6250], [6750, 3500]], // bottom
];
const RIVER = [[-7600, 7300], [-6300, 5600], [-3200, 2600], [0, 0], [3000, -2700], [5900, -6100], [7500, -7600]];

function SchematicMap() {
    return (
        <g aria-hidden="true">
            <rect className="map-ground" x={-EXTENT} y={-EXTENT} width={2 * EXTENT} height={2 * EXTENT} rx="400" />
            <path className="map-base radiant" d={`M${-EXTENT},${EXTENT} L${-EXTENT},3700 L-3600,3700 L-3600,${EXTENT} Z`} />
            <path className="map-base dire" d={`M${EXTENT},${-EXTENT} L${EXTENT},-3300 L3300,-3300 L3300,${-EXTENT} Z`} />
            <path className="map-river" d={path(RIVER)} />
            {LANES.map(lane => <path key={lane[0].join()} className="map-lane" d={path(lane)} />)}
            <text className="map-label" x={-7600} y={7700}>Radiant</text>
            <text className="map-label" x={7600} y={-7300} textAnchor="end">Dire</text>
        </g>
    );
}

export default SchematicMap;
