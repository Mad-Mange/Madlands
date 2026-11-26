import os
import shutil
import stat
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

from orchestrator import gitops  # noqa: E402


def _long(p):
    return "\\\\?\\" + str(p) if os.name == "nt" else str(p)


class RemoveTests(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="aicritic-gitops-")).resolve()

    def tearDown(self):
        shutil.rmtree(_long(self.tmp), ignore_errors=True)

    def test_deletes_paths_beyond_260_characters_and_read_only_files(self):
        root = self.tmp / "worktree"
        deep = root
        while len(str(deep)) < 320:
            deep = deep / "a_rather_long_folder_name_like_unity_library_uses"
        os.makedirs(_long(deep))
        leaf = _long(deep / "artifact.bin")
        with open(leaf, "wb") as f:
            f.write(b"x")
        os.chmod(leaf, stat.S_IREAD)
        gitops._rmtree_long(root)
        self.assertFalse(os.path.exists(_long(root)))

    def test_refuses_to_delete_outside_the_allowed_root(self):
        outside = self.tmp / "elsewhere"
        outside.mkdir()
        with self.assertRaises(gitops.GitError):
            gitops.remove_worktree(self.tmp, outside, self.tmp / "worktrees")
        self.assertTrue(outside.exists())


if __name__ == "__main__":
    unittest.main()
