import { Link } from "react-router";
import { ArrowLeft } from "lucide-react";
import { t } from "@/shared/locales/i18n";

/**
 * Legal pages for the self-hosted distribution. Callu is MIT-licensed and ships
 * as software the operator runs on their own infrastructure — so these pages
 * frame the operator as the data controller and Callu.app as the upstream
 * software provider with no operational relationship to the running instance.
 * Recipients of this advice should still consult their own counsel; the text
 * is informational, not legal advice. References from initial-setup footer.
 */

function LegalLayout({ titleKey, children }: { titleKey: string; children: React.ReactNode }) {
    return (
        <div className="min-h-screen bg-gray-50 dark:bg-gray-900 py-12 px-4">
            <div className="mx-auto max-w-3xl">
                <div className="mb-6">
                    <Link
                        to="/login"
                        className="inline-flex items-center text-sm text-brand-600 hover:text-brand-700 dark:text-brand-400"
                    >
                        <ArrowLeft className="w-4 h-4 mr-1.5" />
                        {t("legal.backToApp")}
                    </Link>
                </div>
                <div className="rounded-lg border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 p-8 shadow-sm">
                    <h1 className="text-2xl font-semibold text-gray-900 dark:text-gray-100 mb-1">
                        {t(titleKey)}
                    </h1>
                    <p className="text-xs text-gray-500 dark:text-gray-400 mb-8">
                        {t("legal.lastUpdated")}: 2026-05-24
                    </p>
                    <div className="prose prose-sm dark:prose-invert max-w-none space-y-5 text-gray-700 dark:text-gray-300 text-sm leading-relaxed">
                        {children}
                    </div>
                </div>
                <p className="text-center text-xs text-gray-500 dark:text-gray-400 mt-6">
                    {t("legal.openSourceFooter")}
                </p>
            </div>
        </div>
    );
}

function Section({ titleKey, children }: { titleKey: string; children: React.ReactNode }) {
    return (
        <section>
            <h2 className="text-base font-semibold text-gray-900 dark:text-gray-100 mt-6 mb-2">
                {t(titleKey)}
            </h2>
            <div className="space-y-2">{children}</div>
        </section>
    );
}

export function TermsOfService() {
    return (
        <LegalLayout titleKey="legal.terms.title">
            <p>{t("legal.terms.intro")}</p>

            <Section titleKey="legal.terms.softwareLicenseHeading">
                <p>{t("legal.terms.softwareLicenseBody")}</p>
            </Section>

            <Section titleKey="legal.terms.operatorResponsibilityHeading">
                <p>{t("legal.terms.operatorResponsibilityBody")}</p>
                <ul className="list-disc pl-5 space-y-1">
                    <li>{t("legal.terms.operatorBullet1")}</li>
                    <li>{t("legal.terms.operatorBullet2")}</li>
                    <li>{t("legal.terms.operatorBullet3")}</li>
                    <li>{t("legal.terms.operatorBullet4")}</li>
                </ul>
            </Section>

            <Section titleKey="legal.terms.noWarrantyHeading">
                <p className="uppercase font-medium">{t("legal.terms.noWarrantyBody")}</p>
            </Section>

            <Section titleKey="legal.terms.limitationHeading">
                <p className="uppercase font-medium">{t("legal.terms.limitationBody")}</p>
            </Section>

            <Section titleKey="legal.terms.thirdPartyHeading">
                <p>{t("legal.terms.thirdPartyBody")}</p>
            </Section>

            <Section titleKey="legal.terms.acceptableUseHeading">
                <p>{t("legal.terms.acceptableUseBody")}</p>
            </Section>

            <Section titleKey="legal.terms.changesHeading">
                <p>{t("legal.terms.changesBody")}</p>
            </Section>

            <Section titleKey="legal.terms.contactHeading">
                <p>{t("legal.terms.contactBody")}</p>
            </Section>
        </LegalLayout>
    );
}

export function PrivacyPolicy() {
    return (
        <LegalLayout titleKey="legal.privacy.title">
            <p>{t("legal.privacy.intro")}</p>

            <Section titleKey="legal.privacy.controllerHeading">
                <p>{t("legal.privacy.controllerBody")}</p>
            </Section>

            <Section titleKey="legal.privacy.dataCollectedHeading">
                <p>{t("legal.privacy.dataCollectedBody")}</p>
                <ul className="list-disc pl-5 space-y-1">
                    <li><strong>{t("legal.privacy.dataAccountLabel")}</strong> — {t("legal.privacy.dataAccountBody")}</li>
                    <li><strong>{t("legal.privacy.dataIncidentLabel")}</strong> — {t("legal.privacy.dataIncidentBody")}</li>
                    <li><strong>{t("legal.privacy.dataContactLabel")}</strong> — {t("legal.privacy.dataContactBody")}</li>
                    <li><strong>{t("legal.privacy.dataAuditLabel")}</strong> — {t("legal.privacy.dataAuditBody")}</li>
                    <li><strong>{t("legal.privacy.dataAuthLabel")}</strong> — {t("legal.privacy.dataAuthBody")}</li>
                </ul>
            </Section>

            <Section titleKey="legal.privacy.noTelemetryHeading">
                <p>{t("legal.privacy.noTelemetryBody")}</p>
            </Section>

            <Section titleKey="legal.privacy.thirdPartyHeading">
                <p>{t("legal.privacy.thirdPartyBody")}</p>
                <ul className="list-disc pl-5 space-y-1">
                    <li>{t("legal.privacy.thirdPartyBullet1")}</li>
                    <li>{t("legal.privacy.thirdPartyBullet2")}</li>
                    <li>{t("legal.privacy.thirdPartyBullet3")}</li>
                </ul>
            </Section>

            <Section titleKey="legal.privacy.retentionHeading">
                <p>{t("legal.privacy.retentionBody")}</p>
            </Section>

            <Section titleKey="legal.privacy.securityHeading">
                <p>{t("legal.privacy.securityBody")}</p>
            </Section>

            <Section titleKey="legal.privacy.userRightsHeading">
                <p>{t("legal.privacy.userRightsBody")}</p>
            </Section>

            <Section titleKey="legal.privacy.cookiesHeading">
                <p>{t("legal.privacy.cookiesBody")}</p>
            </Section>

            <Section titleKey="legal.privacy.changesHeading">
                <p>{t("legal.privacy.changesBody")}</p>
            </Section>
        </LegalLayout>
    );
}
