// Finds the adb program and checks the APK paths the tests are given, so that no program is looked up through PATH
// and nothing taken from the command line can be read by adb as an option.
import { existsSync, statSync } from 'node:fs';
import { isAbsolute, join, resolve, sep } from 'node:path';

// adb from the Android SDK the CI runner installs (ANDROID_HOME, or ANDROID_SDK_ROOT on older images).
export function adbPath() {
  const sdk = process.env.ANDROID_HOME || process.env.ANDROID_SDK_ROOT;
  if (!sdk || !isAbsolute(sdk)) {
    throw new Error('ANDROID_HOME must be set to the absolute path of the Android SDK.');
  }
  const adb = join(sdk, 'platform-tools', process.platform === 'win32' ? 'adb.exe' : 'adb');
  if (!existsSync(adb)) {
    throw new Error(`adb not found at ${adb}.`);
  }
  return adb;
}

// An APK path from the command line, as an absolute path to an existing .apk file inside the working directory (the CI
// workspace); anything else stops the test, so no argument can point adb at another file or be read as an option.
export function apkArgument(value, name) {
  if (typeof value !== 'string' || !value.toLowerCase().endsWith('.apk')) {
    throw new Error(`${name} must be the path of an .apk file.`);
  }
  const base = resolve(process.cwd());
  const path = resolve(base, value);
  if (!path.startsWith(base + sep)) {
    throw new Error(`${name} must be inside the working directory ${base}.`);
  }
  if (!existsSync(path) || !statSync(path).isFile()) {
    throw new Error(`${name} does not exist: ${path}`);
  }
  return path;
}
