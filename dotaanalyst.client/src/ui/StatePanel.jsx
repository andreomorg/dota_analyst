// A state that replaces content (Stitch state libraries): a centred card with an icon, a title, the explanation and
// the actions the visitor can really take. tone: 'neutral', 'loss' (error) or 'you'.

const ICONS = {
    search: <><circle cx="10.5" cy="10.5" r="6" /><path d="M15 15l5 5M8 10.5h5" /></>,
    user: <><circle cx="10" cy="8" r="4" /><path d="M3 20a7 7 0 0 1 14 0M17 11l2 2 3-4" /></>,
    alert: <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01" />,
    invalid: <><rect x="4" y="3" width="16" height="18" rx="2" /><path d="M9 9l6 6M15 9l-6 6" /></>,
    chart: <path d="M3 3v18h18M7 15l4-4 3 3 6-6" />,
    info: <><circle cx="12" cy="12" r="10" /><path d="M12 16v-4M12 8h.01" /></>,
};

export function StateIcon({ icon }) {
    return (
        <svg className="state-svg" viewBox="0 0 24 24" aria-hidden="true">
            {ICONS[icon]}
        </svg>
    );
}

function StatePanel({ icon, tone = 'neutral', code, title, titleAs: Title = 'h2', children, actions, role }) {
    return (
        <div className={`state-panel tone-${tone}`} role={role}>
            {code
                ? <p className="state-code mono" aria-hidden="true">{code}</p>
                : icon && <span className="state-icon"><StateIcon icon={icon} /></span>}
            {title && <Title className="state-title">{title}</Title>}
            {children && <div className="state-text">{children}</div>}
            {actions && <div className="state-actions">{actions}</div>}
        </div>
    );
}

export default StatePanel;
