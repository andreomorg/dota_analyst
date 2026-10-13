import { useSearchParams } from 'react-router';

// The link to a tab of the analysis: keeps ?player= (whose point of view) and only changes ?tab= (none = overview).
export function useTabLink() {
    const [searchParams] = useSearchParams();
    return key => {
        const params = new URLSearchParams(searchParams);
        if (key === 'overview') {
            params.delete('tab');
        } else {
            params.set('tab', key);
        }
        const search = params.toString();
        return { search: search ? `?${search}` : '' };
    };
}
