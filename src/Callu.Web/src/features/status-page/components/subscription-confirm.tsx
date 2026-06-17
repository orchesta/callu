import { useEffect, useState } from 'react';
import { useSearchParams, Link } from 'react-router';
import { statusPageApi } from '../api/status-page.api';

/**
 * Landing page for the double opt-in subscription confirmation flow.
 * The backend sends an email with a link like
 *   /status/subscribe-confirm?token=<sha256-hex>
 * Idempotent: re-visiting an already-confirmed (or expired/unknown) token
 * returns the same generic success message — we don't leak subscription state
 * to anonymous visitors.
 */
export function SubscriptionConfirm() {
    const [params] = useSearchParams();
    const token = params.get('token') ?? '';
    const [status, setStatus] = useState<'pending' | 'success' | 'error'>('pending');
    const [message, setMessage] = useState<string>('');

    useEffect(() => {
        if (!token) {
            setStatus('error');
            setMessage('Confirmation link is missing the token. Please re-open the email link.');
            return;
        }
        let cancelled = false;
        (async () => {
            try {
                const resp = await statusPageApi.confirmSubscription(token);
                if (cancelled) return;
                setStatus('success');
                setMessage(resp?.data?.message ?? 'Subscription confirmed.');
            } catch {
                if (cancelled) return;
                setStatus('error');
                setMessage('We could not confirm your subscription. Please request a new link.');
            }
        })();
        return () => { cancelled = true; };
    }, [token]);

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 px-4">
            <div className="w-full max-w-md rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-8 shadow-sm">
                <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-3">
                    {status === 'pending' && 'Confirming your subscription…'}
                    {status === 'success' && 'You are subscribed'}
                    {status === 'error' && 'Confirmation failed'}
                </h1>
                <p className="text-sm text-gray-600 dark:text-gray-300">
                    {status === 'pending' ? 'Please wait while we verify your link.' : message}
                </p>
                <div className="mt-6">
                    <Link
                        to="/status"
                        className="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        ← Back to status page
                    </Link>
                </div>
            </div>
        </div>
    );
}

/**
 * One-click unsubscribe landing — same UX shape as Confirm, different copy.
 */
export function SubscriptionUnsubscribe() {
    const [params] = useSearchParams();
    const token = params.get('token') ?? '';
    const [status, setStatus] = useState<'pending' | 'success' | 'error'>('pending');
    const [message, setMessage] = useState<string>('');

    useEffect(() => {
        if (!token) {
            setStatus('error');
            setMessage('Unsubscribe link is missing the token.');
            return;
        }
        let cancelled = false;
        (async () => {
            try {
                const resp = await statusPageApi.unsubscribeByToken(token);
                if (cancelled) return;
                setStatus('success');
                setMessage(resp?.data?.message ?? 'You have been unsubscribed.');
            } catch {
                if (cancelled) return;
                setStatus('error');
                setMessage('We could not process the unsubscribe request. The link may have expired.');
            }
        })();
        return () => { cancelled = true; };
    }, [token]);

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 px-4">
            <div className="w-full max-w-md rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-8 shadow-sm">
                <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-3">
                    {status === 'pending' && 'Processing your request…'}
                    {status === 'success' && 'Unsubscribed'}
                    {status === 'error' && 'Unsubscribe failed'}
                </h1>
                <p className="text-sm text-gray-600 dark:text-gray-300">
                    {status === 'pending' ? 'Please wait.' : message}
                </p>
                <div className="mt-6">
                    <Link
                        to="/status"
                        className="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        ← Back to status page
                    </Link>
                </div>
            </div>
        </div>
    );
}
