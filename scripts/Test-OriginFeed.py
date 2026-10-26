"""Temporary fixtures only. Does not invoke an executor, SSH, or any network API."""
import contextlib
import copy
import hashlib
import importlib.util
import json
import os
import pathlib
import stat
import sys
import tempfile
import unittest
from unittest import mock

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('origin_feed', pathlib.Path(__file__).with_name('origin_feed.py'))
origin = importlib.util.module_from_spec(spec)
spec.loader.exec_module(origin)


class OriginFeedTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='clipshot-origin-fixture-')
        self.base = pathlib.Path(self.temporary.name)
        self.root = self.base / 'web'
        self.root.mkdir()
        self.backups = self.base / 'private-maintenance'
        self.patches = [mock.patch.object(origin, 'BACKUP_ROOT', self.backups)]
        self.syncs = []
        if os.name != 'posix':
            @contextlib.contextmanager
            def fixture_lock(_):
                yield
            self.patches += [mock.patch.object(origin, '_publication_lock', fixture_lock),
                             mock.patch.object(origin, '_fsync_directory', lambda path: self.syncs.append(path))]
        for patch in self.patches:
            patch.start()
        self.feed = self.make_feed('9.8.7')
        self.target = self.root / 'update-feed.json'
        self.previous = b'{"schema":1,"stable":null,"rollback":null}\n'
        self.target.write_bytes(self.previous)

    def tearDown(self):
        for patch in reversed(self.patches):
            patch.stop()
        self.temporary.cleanup()

    def release(self, version, flavor='legacy', host='github'):
        name = '{}-Setup-{}.exe'.format(origin.PREFIX[flavor], version)
        raw = (version + '/' + flavor).encode() * 2048
        directory = self.root / ('v' + version)
        directory.mkdir(exist_ok=True)
        (directory / name).write_bytes(raw)
        url = 'https://github.com/gromit7/ClipShot/releases/download/v{}/{}'.format(version, name)
        if host != 'github':
            url = 'https://{}.data-clouds.ru/clipshot/v{}/{}'.format(host, version, name)
        return {'draft': False, 'prerelease': False, 'tag_name': 'v' + version, 'assets': [
            {'name': name, 'size': len(raw), 'digest': 'sha256:' + hashlib.sha256(raw).hexdigest(), 'browser_download_url': url}]}

    def make_feed(self, version):
        return {'schema': 1, 'stable': self.release(version), 'rollback': None,
                'variants': {'win10': self.release(version, 'win10'), 'win11': self.release(version, 'win11')}}

    def raw(self, feed=None):
        return json.dumps(self.feed if feed is None else feed, ensure_ascii=False).encode('utf-8') + b'\n'

    def snapshot(self):
        return {str(path.relative_to(self.base)): (path.read_bytes(), path.stat().st_mtime_ns, stat.S_IMODE(path.stat().st_mode))
                for path in self.base.rglob('*') if path.is_file()}

    def test_atomic_publish_preserves_exact_bytes_and_private_previous_feed(self):
        raw = self.raw() + b' \n'
        replace = os.replace
        with mock.patch.object(origin.os, 'replace', wraps=replace) as swap:
            receipt = origin.publish_origin_feed(self.root, raw)
        self.assertEqual(1, swap.call_count)
        self.assertEqual(self.root, pathlib.Path(swap.call_args[0][0]).parent)
        self.assertEqual(str(self.target), swap.call_args[0][1])
        self.assertEqual(raw, self.target.read_bytes())
        self.assertEqual(hashlib.sha256(raw).hexdigest(), receipt['feed_sha256'])
        self.assertEqual(3, receipt['packages_count'])
        self.assertTrue(receipt['atomic_replace'])
        self.assertTrue(all(package['verified'] for package in receipt['packages']))
        backup = self.backups / receipt['backup_id'] / 'update-feed.json'
        self.assertEqual(self.previous, backup.read_bytes())
        self.assertFalse(list(self.root.glob('.origin-feed-*')))
        if os.name == 'posix':
            self.assertEqual(0o644, stat.S_IMODE(self.target.stat().st_mode))
            self.assertEqual(0o600, stat.S_IMODE(backup.stat().st_mode))
            self.assertEqual(0o700, stat.S_IMODE(backup.parent.stat().st_mode))

    def test_hash_failure_does_not_replace_feed_or_create_backup(self):
        self.feed['variants']['win11']['assets'][0]['digest'] = 'sha256:' + '0' * 64
        with mock.patch.object(origin.os, 'replace') as swap:
            with self.assertRaisesRegex(ValueError, 'sha256'):
                origin.publish_origin_feed(self.root, self.raw())
        self.assertEqual(self.previous, self.target.read_bytes())
        self.assertFalse(self.backups.exists())
        swap.assert_not_called()

    def test_check_only_is_byte_exact_and_does_not_mutate_any_fixture(self):
        raw = self.raw()
        self.target.write_bytes(raw)
        before = self.snapshot()
        with mock.patch.object(origin, '_publication_lock', side_effect=AssertionError('check acquired a write lock')):
            with mock.patch.object(origin.os, 'replace') as swap:
                receipt = origin.publish_origin_feed(self.root, raw, True)
        self.assertEqual(before, self.snapshot())
        self.assertEqual(3, receipt['packages_count'])
        self.assertFalse(receipt['atomic_replace'])
        self.assertIsNone(receipt['backup_id'])
        swap.assert_not_called()
        with self.assertRaisesRegex(ValueError, 'byte-mismatch'):
            origin.publish_origin_feed(self.root, raw + b' ', True)
        self.assertEqual(before, self.snapshot())

    def test_check_only_rejects_corrupt_existing_package_without_changing_feed(self):
        raw = self.raw()
        self.target.write_bytes(raw)
        package = self.root / 'v9.8.7' / self.feed['stable']['assets'][0]['name']
        package.write_bytes(b'X' * package.stat().st_size)
        with self.assertRaisesRegex(ValueError, 'sha256'):
            origin.publish_origin_feed(self.root, raw, True)
        self.assertEqual(raw, self.target.read_bytes())
        self.assertFalse(self.backups.exists())

    def test_null_revoke_and_variant_revoke_are_valid(self):
        revoke = {'schema': 1, 'stable': None, 'rollback': None}
        result = origin.publish_origin_feed(self.root, self.raw(revoke))
        self.assertEqual(0, result['packages_count'])
        self.assertEqual(self.raw(revoke), self.target.read_bytes())
        self.feed['variants']['win10'] = None
        self.assertEqual(2, origin.publish_origin_feed(self.root, self.raw())['packages_count'])
        revoke['variants'] = {'win10': None, 'win11': None}
        self.assertEqual(0, origin.publish_origin_feed(self.root, self.raw(revoke))['packages_count'])

    def test_legacy_rollback_with_real_revision_policy_shape_and_russian_reason(self):
        self.feed['rollback'] = {'release': self.release('9.8.6'), 'policy': {
            'schema': 1, 'revision': 1, 'channel': 'stable', 'action': 'rollback',
            'targetVersion': '9.8.6', 'affectedVersions': ['9.8.7', '9.8.8'], 'reason': 'Синтетический откат'}}
        raw = self.raw()
        result = origin.publish_origin_feed(self.root, raw)
        self.assertEqual(4, result['packages_count'])
        self.assertEqual(raw, self.target.read_bytes())
        self.feed['rollback']['policy']['revision'] = 0
        with self.assertRaisesRegex(ValueError, 'revision'):
            origin.publish_origin_feed(self.root, self.raw())
        self.assertEqual(raw, self.target.read_bytes())

    def test_exact_rf_and_de_urls_are_metadata_only(self):
        self.feed['stable'] = self.release('9.8.7', host='bot')
        self.feed['variants']['win11'] = self.release('9.8.7', 'win11', 'tg')
        result = origin.publish_origin_feed(self.root, self.raw())
        self.assertTrue(all(package['url'].startswith('https://bot.data-clouds.ru/') for package in result['packages']))

    def test_bad_metadata_and_variant_mismatch_fail_before_any_atomic_replace(self):
        mutations = [
            lambda feed: feed.update(schema=True),
            lambda feed: feed['stable'].update(draft=True),
            lambda feed: feed['stable']['assets'][0].update(name='../foreign.exe'),
            lambda feed: feed['stable']['assets'][0].update(size=True),
            lambda feed: feed['stable']['assets'][0].update(size=1024.0),
            lambda feed: feed['stable']['assets'][0].update(digest='bad'),
            lambda feed: feed['stable']['assets'][0].update(browser_download_url='https://evil.invalid/setup.exe'),
            lambda feed: feed['variants']['win10'].update(tag_name='v9.8.6'),
            lambda feed: feed['variants'].pop('win10'),
            lambda feed: feed['stable'].update(tag_name='v09.8.7'),
        ]
        for mutate in mutations:
            with self.subTest(mutation=mutate):
                feed = copy.deepcopy(self.feed)
                mutate(feed)
                with mock.patch.object(origin.os, 'replace') as swap:
                    with self.assertRaises(ValueError):
                        origin.publish_origin_feed(self.root, self.raw(feed))
                    swap.assert_not_called()
                self.assertEqual(self.previous, self.target.read_bytes())
                self.assertFalse(self.backups.exists())

    def test_invalid_raw_and_duplicate_json_keys_are_rejected(self):
        for raw in (b' ' * 32769, b'\xff', b'\xef\xbb\xbf' + self.raw(), b'{"schema":1,"schema":1,"stable":null,"rollback":null}'):
            with self.subTest(raw_size=len(raw)):
                with self.assertRaises(ValueError):
                    origin.publish_origin_feed(self.root, raw)
                self.assertEqual(self.previous, self.target.read_bytes())

    def test_replace_failure_keeps_previous_feed_and_cleans_only_own_staging_file(self):
        with mock.patch.object(origin.os, 'replace', side_effect=OSError('injected replace failure')):
            with self.assertRaises(OSError):
                origin.publish_origin_feed(self.root, self.raw())
        self.assertEqual(self.previous, self.target.read_bytes())
        self.assertFalse(list(self.root.glob('.origin-feed-*')))
        self.assertEqual([self.previous], [path.read_bytes() for path in self.backups.glob('*/update-feed.json')])

    def test_failed_private_backup_prevents_publication(self):
        with mock.patch.object(origin, '_backup', side_effect=PermissionError('injected backup failure')):
            with mock.patch.object(origin.os, 'replace') as swap:
                with self.assertRaises(PermissionError):
                    origin.publish_origin_feed(self.root, self.raw())
        self.assertEqual(self.previous, self.target.read_bytes())
        swap.assert_not_called()


if __name__ == '__main__':
    print('No network/SSH. Windows mocks only Linux flock and directory fsync; file hashes and os.replace are real.', flush=True)
    unittest.main(verbosity=2)
