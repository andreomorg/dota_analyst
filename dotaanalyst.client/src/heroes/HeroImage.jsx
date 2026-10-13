import { useState } from 'react';
import fallbackHeroImage from '../assets/hero.png';

function HeroImage({ heroId, kind, className }) {
    const [failedSource, setFailedSource] = useState(null);
    const source = Number.isInteger(heroId) && heroId > 0
        ? `/hero-images/${kind}/${heroId}.webp`
        : fallbackHeroImage;
    const displayedSource = failedSource === source ? fallbackHeroImage : source;

    return (
        <img
            className={className}
            src={displayedSource}
            alt=""
            aria-hidden="true"
            loading="lazy"
            decoding="async"
            onError={() => setFailedSource(source)}
        />
    );
}

export function HeroIcon({ heroId }) {
    return <HeroImage heroId={heroId} kind="icons" className="hero-image hero-icon" />;
}

export function HeroPortrait({ heroId }) {
    return <HeroImage heroId={heroId} kind="full" className="hero-image hero-portrait" />;
}
