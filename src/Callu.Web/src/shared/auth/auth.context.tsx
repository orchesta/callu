import { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authService, type AuthUser } from './auth.service';
import { AUTH_TOKEN_KEY } from '@/shared/config';

interface AuthContextValue {
    user: AuthUser | null;
    isAuthenticated: boolean;
    isLoading: boolean;
    login: (email: string, password: string) => Promise<void>;
    logout: () => Promise<void>;
    /** Force re-check auth state (e.g. after external token refresh) */
    refreshAuthState: () => void;
    /** Pull the stored name and email again, e.g. after the user edits their own profile. */
    refreshIdentity: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
    const queryClient = useQueryClient();
    const [user, setUser] = useState<AuthUser | null>(() => authService.getCurrentUser());
    const [isLoading, setIsLoading] = useState(true);

    const isAuthenticated = user !== null;

    const refreshAuthState = useCallback(() => {
        const currentUser = authService.getCurrentUser();
        setUser(currentUser);
    }, []);

    const refreshIdentity = useCallback(async () => {
        const stored = await authService.fetchStoredIdentity();
        if (!stored) return;

        setUser((current) => {
            if (!current) return current;
            if (current.name === stored.name && current.email === stored.email) return current;
            return { ...current, name: stored.name, email: stored.email };
        });
    }, []);

    // A stale access token does not mean the session is over: the refresh cookie
    // outlives it by days. Try one silent refresh before deciding we are logged out.
    useEffect(() => {
        let cancelled = false;

        void (async () => {
            const token = authService.getAccessToken();
            if (token && !authService.isAuthenticated()) {
                try {
                    await authService.refreshAccessToken();
                } catch {
                    // Server unreachable. Nothing to decide from that, and the app must still
                    // finish loading — leaving isLoading set would pin it on the splash screen.
                }
            }

            if (cancelled) return;
            refreshAuthState();
            setIsLoading(false);

            // Not awaited before the app renders: the token already gives us a usable identity,
            // and a slow or failed read here must not hold up the first paint.
            void refreshIdentity();
        })();

        return () => { cancelled = true; };
    }, [refreshAuthState, refreshIdentity]);

    useEffect(() => {
        const handleStorageChange = (e: StorageEvent) => {
            if (e.key === AUTH_TOKEN_KEY || e.key === null) {
                refreshAuthState();
            }
        };

        window.addEventListener('storage', handleStorageChange);
        return () => window.removeEventListener('storage', handleStorageChange);
    }, [refreshAuthState]);

    // Only these two transitions clear the cache — a token refresh is not one of them.
    const login = useCallback(async (email: string, password: string) => {
        await authService.login(email, password);
        queryClient.clear();
        refreshAuthState();
    }, [queryClient, refreshAuthState]);

    const logout = useCallback(async () => {
        queryClient.clear();
        await authService.logout();
    }, [queryClient]);

    const value = useMemo<AuthContextValue>(() => ({
        user,
        isAuthenticated,
        isLoading,
        login,
        logout,
        refreshAuthState,
        refreshIdentity,
    }), [user, isAuthenticated, isLoading, login, logout, refreshAuthState, refreshIdentity]);

    return (
        <AuthContext value={value}>
            {children}
        </AuthContext>
    );
}

export function useAuth(): AuthContextValue {
    const context = useContext(AuthContext);
    if (!context) {
        throw new Error('useAuth must be used within an AuthProvider');
    }
    return context;
}
