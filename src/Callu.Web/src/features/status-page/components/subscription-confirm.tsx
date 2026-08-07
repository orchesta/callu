import { useEffect, useState } from 'react';
import { useSearchParams, Link } from 'react-router';
import { t } from '@/shared/locales/i18n';
import { statusPageApi } from '../api/status-page.api';

/** Landing page for the emailed double opt-in link. Idempotent, and every token outcome gets the same
 * generic message so subscription state is not leaked to anonymous visitors. */
export function SubscriptionConfirm() {
    const [params] = useSearchParams();
    const token = params.get('token') ?? '';
    const [status, setStatus] = useState<'pending' | 'success' | 'error'>('pending');
    const [message, setMessage] = useState<string>('');

    useEffect(() => {
        if (!token) {
            setStatus('error');
            setMessage(t('statusSubs.missingToken'));
            return;
        }
        let cancelled = false;
        (async () => {
            try {
                const resp = await statusPageApi.confirmSubscription(token);
                if (cancelled) return;
                setStatus('success');
                setMessage(resp?.data?.message ?? t('statusSubs.confirmedFallback'));
            } catch {
                if (cancelled) return;
                setStatus('error');
                setMessage(t('statusSubs.confirmFailedBody'));
            }
        })();
        return () => { cancelled = true; };
    }, [token]);

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 px-4">
            <div className="w-full max-w-md rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-8 shadow-sm">
                <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-3">
                    {status === 'pending' && t('statusSubs.confirmingTitle')}
                    {status === 'success' && t('statusSubs.confirmedTitle')}
                    {status === 'error' && t('statusSubs.confirmFailedTitle')}
                </h1>
                <p className="text-sm text-gray-600 dark:text-gray-300">
                    {status === 'pending' ? t('statusSubs.pendingBody') : message}
                </p>
                <div className="mt-6">
                    <Link
                        to="/status"
                        className="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        {t('statusSubs.backToStatus')}
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
            setMessage(t('statusSubs.unsubMissingToken'));
            return;
        }
        let cancelled = false;
        (async () => {
            try {
                const resp = await statusPageApi.unsubscribeByToken(token);
                if (cancelled) return;
                setStatus('success');
                setMessage(resp?.data?.message ?? t('statusSubs.unsubDoneFallback'));
            } catch {
                if (cancelled) return;
                setStatus('error');
                setMessage(t('statusSubs.unsubFailedBody'));
            }
        })();
        return () => { cancelled = true; };
    }, [token]);

    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-gray-900 px-4">
            <div className="w-full max-w-md rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-8 shadow-sm">
                <h1 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-3">
                    {status === 'pending' && t('statusSubs.unsubProcessingTitle')}
                    {status === 'success' && t('statusSubs.unsubDoneTitle')}
                    {status === 'error' && t('statusSubs.unsubFailedTitle')}
                </h1>
                <p className="text-sm text-gray-600 dark:text-gray-300">
                    {status === 'pending' ? t('statusSubs.pleaseWait') : message}
                </p>
                <div className="mt-6">
                    <Link
                        to="/status"
                        className="text-sm font-medium text-brand-600 hover:text-brand-700 dark:text-brand-400">
                        {t('statusSubs.backToStatus')}
                    </Link>
                </div>
            </div>
        </div>
    );
}
