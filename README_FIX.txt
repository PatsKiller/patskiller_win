PatsKiller Pro - Fix Pack (Build + Auth UI)

What this fixes
1) GitHub Actions build error CS0111/CS0101 (duplicate GoogleLoginForm)
   - Your repo now has TWO GoogleLoginForm.cs files (root + Forms). SDK-style csproj compiles both => duplicates.
   - Fix: csproj now EXCLUDES the root file (GoogleLoginForm.cs) so only the Forms version is compiled.

2) Build error: 'LicenseService' missing CustomerName
   - Adds a back-compat alias property CustomerName => LicensedTo.
   - Updates LicenseActivationForm to use the correct property safely.

Files included
- PatsKillerPro/PatsKillerPro.csproj (adds: <Compile Remove="GoogleLoginForm.cs" />)
- PatsKillerPro/Forms/GoogleLoginForm.cs (uses /api/desktop-auth route)
- PatsKillerPro/Services/LicenseService.cs (adds CustomerName alias)
- PatsKillerPro/Forms/LicenseActivationForm.cs (aligns with LicenseService)

Recommended clean-up (optional but best practice)
- After the build passes, delete the old duplicate file:
    PatsKillerPro/GoogleLoginForm.cs
  This keeps the repo tidy and removes ambiguity.

How to apply
- Copy the folders into your repo root (merge/overwrite).
- Commit and push.

