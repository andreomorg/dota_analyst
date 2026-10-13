// "16 / 3 / 14" with kills in green and deaths in red, in the design's mono well.
function Kda({ kills, deaths, assists, size }) {
    return (
        <span className={`kda${size === 'large' ? ' kda-large' : ''}`}>
            <span className="kda-kills">{kills}</span>
            <span className="kda-sep" aria-hidden="true">/</span>
            <span className="kda-deaths">{deaths}</span>
            <span className="kda-sep" aria-hidden="true">/</span>
            <span className="kda-assists">{assists}</span>
        </span>
    );
}

export default Kda;
