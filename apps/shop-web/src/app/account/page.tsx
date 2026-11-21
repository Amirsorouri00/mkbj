'use client';

import { ShopMeDocument, ShopUpdateProfileDocument, type ShopMeQuery, type UpdateProfileInput } from '@mkbj/api-graphql';
import { CheckCircle2, LogOut } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { useShopAuth } from '@/auth/shop-auth-provider';
import { AuthGate } from '@/components/auth-gate';
import { shopClientRequest } from '@/lib/client-graphql';

const optionalField = (data: FormData, name: string): string | null => {
  const value = data.get(name);
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null;
};

function billingAddress(data: FormData): UpdateProfileInput['billingAddress'] {
  const line1 = optionalField(data, 'line1');
  const line2 = optionalField(data, 'line2');
  const city = optionalField(data, 'city');
  const postalCode = optionalField(data, 'postalCode');
  const countryIso2 = optionalField(data, 'countryIso2')?.toUpperCase() ?? null;
  if (![line1, line2, city, postalCode, countryIso2].some(Boolean)) return null;
  if (!line1 || !city || !postalCode || !countryIso2) {
    throw new Error('Complete address line 1, city, postal code, and country before saving.');
  }
  return { line1, line2, city, postalCode, countryIso2 };
}

export default function AccountPage() {
  const auth = useShopAuth();
  return (
    <div className="site-container account-page">
      <AuthGate returnTo="/account" title="Sign in to manage your profile">
        <Profile getToken={auth.getToken} logout={auth.logout} />
      </AuthGate>
    </div>
  );
}

function Profile({ getToken, logout }: { getToken: () => Promise<string | null>; logout: () => Promise<void> }) {
  const [profile, setProfile] = useState<ShopMeQuery['me'] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const load = useCallback(async () => {
    const token = await getToken();
    if (!token) return;
    setError(null);
    try {
      setProfile((await shopClientRequest(ShopMeDocument, {}, token)).me);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not load your profile.');
    }
  }, [getToken]);

  useEffect(() => { void load(); }, [load]);
  if (!profile && !error) return <div className="account-state" role="status">Loading your profile…</div>;
  if (!profile) return <div className="account-state"><h1>Your profile is unavailable</h1><p role="alert">{error}</p><button className="button button--secondary" onClick={() => void load()} type="button">Try again</button></div>;

  return (
    <div className="profile-page">
      <header className="account-heading">
        <div><p className="eyebrow">Your account</p><h1>Profile</h1><p>{profile.email} {profile.emailVerified && <span className="verified-label"><CheckCircle2 aria-hidden="true" /> Verified</span>}</p></div>
        <button className="button button--quiet" onClick={() => void logout()} type="button"><LogOut aria-hidden="true" /> Sign out</button>
      </header>
      {error && <div className="inline-error" role="alert">{error}</div>}
      {saved && <div className="inline-success" role="status">Your profile was updated.</div>}
      <form
        className="profile-form"
        onSubmit={(event) => {
          event.preventDefault();
          setError(null);
          setSaved(false);
          setSaving(true);
          const form = event.currentTarget;
          void (async () => {
            const token = await getToken();
            if (!token) { setSaving(false); return; }
            const data = new FormData(form);
            try {
              const input: UpdateProfileInput = {
                fullName: optionalField(data, 'fullName'),
                companyName: optionalField(data, 'companyName'),
                phone: optionalField(data, 'phone'),
                registrationNumber: optionalField(data, 'registrationNumber'),
                billingAddress: billingAddress(data),
              };
              const result = await shopClientRequest(ShopUpdateProfileDocument, { input }, token);
              setProfile(result.updateProfile);
              setSaved(true);
            } catch (caught) {
              setError(caught instanceof Error ? caught.message : 'Could not update your profile.');
            } finally {
              setSaving(false);
            }
          })();
        }}
      >
        <div className="profile-form__section">
          <h2>Contact details</h2>
          <div className="profile-form__grid">
            <label>Full name<input autoComplete="name" defaultValue={profile.fullName ?? ''} name="fullName" /></label>
            <label>Phone<input autoComplete="tel" defaultValue={profile.phone ?? ''} name="phone" type="tel" /></label>
            <label>Company name <small>Optional</small><input autoComplete="organization" defaultValue={profile.companyName ?? ''} name="companyName" /></label>
            <label>Registration number <small>Optional</small><input defaultValue={profile.registrationNumber ?? ''} name="registrationNumber" /></label>
          </div>
        </div>
        <fieldset className="profile-form__section">
          <legend>Billing address <small>Optional until checkout</small></legend>
          <div className="profile-form__grid">
            <label className="profile-form__wide">Address line 1<input autoComplete="address-line1" defaultValue={profile.billingAddress?.line1 ?? ''} name="line1" /></label>
            <label className="profile-form__wide">Address line 2 <small>Optional</small><input autoComplete="address-line2" defaultValue={profile.billingAddress?.line2 ?? ''} name="line2" /></label>
            <label>City<input autoComplete="address-level2" defaultValue={profile.billingAddress?.city ?? ''} name="city" /></label>
            <label>Postal code<input autoComplete="postal-code" defaultValue={profile.billingAddress?.postalCode ?? ''} name="postalCode" /></label>
            <label>Country code <small>Two letters, such as SG</small><input autoCapitalize="characters" autoComplete="country" defaultValue={profile.billingAddress?.countryIso2 ?? ''} maxLength={2} minLength={2} name="countryIso2" pattern="[A-Za-z]{2}" placeholder="SG" /></label>
          </div>
        </fieldset>
        <button className="button button--primary" disabled={saving} type="submit">{saving ? 'Saving…' : 'Save profile'}</button>
      </form>
    </div>
  );
}
