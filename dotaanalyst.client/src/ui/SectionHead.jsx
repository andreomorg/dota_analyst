import TabIcon from '../analysis/TabIcon';

// The heading of a section of the analysis (the Stitch references): a small green icon, the title, and an optional
// note on the right (children). icon: a key of TabIcon (a tab or a section shape such as "pulse").
function SectionHead({ id, title, icon = 'pulse', children }) {
    return (
        <div className="section-head">
            <h2 id={id}>
                <TabIcon tab={icon} className="section-icon" />
                {title}
            </h2>
            {children && <div className="section-aside">{children}</div>}
        </div>
    );
}

export default SectionHead;
