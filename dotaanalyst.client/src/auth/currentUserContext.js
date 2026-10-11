import { createContext } from 'react';

// Kept apart from the provider component: Vite's fast refresh only works well when
// a .jsx file exports components and nothing else.
export const CurrentUserContext = createContext(null);
