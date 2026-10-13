// The heading of a section of the analysis (the Stitch design): a green bar, the title, and an optional note on the
// right (children).
function SectionHead({ id, title, children }) {
    return (
        <div className="section-head">
            <h2 id={id}>{title}</h2>
            {children && <div className="section-aside">{children}</div>}
        </div>
    );
}

export default SectionHead;
