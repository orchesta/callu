import { createContext, useContext, useState, useEffect, useCallback, useMemo } from 'react';
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
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: React.ReactNode }) {
    const [user, setUser] = useState<AuthUser | null>(() => authService.getCurrentUser());
    const [isLoading, setIsLoading] = useState(true);

    const isAuthenticated = user !== null;

    const refreshAuthState = useCallback(() => {
        const currentUser = authService.getCurrentUser();
        setUser(currentUser);
    }, []);

    useEffect(() => {
        refreshAuthState();
        setIsLoading(false);
    }, [refreshAuthState]);

    useEffect(() => {
        const handleStorageChange = (e: StorageEvent) => {
            if (e.key === AUTH_TOKEN_KEY || e.key === null) {
                refreshAuthState();
            }
        };

        window.addEventListener('storage', handleStorageChange);
        return () => window.removeEventListener('storage', handleStorageChange);
    }, [refreshAuthState]);

    const login = useCallback(async (email: string, password: string) => {
        await authService.login(email, password);
        refreshAuthState();
    }, [refreshAuthState]);

    const logout = useCallback(async () => {
        await authService.logout();
    }, []);

    const value = useMemo<AuthContextValue>(() => ({
        user,
        isAuthenticated,
        isLoading,
        login,
        logout,
        refreshAuthState,
    }), [user, isAuthenticated, isLoading, login, logout, refreshAuthState]);

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
