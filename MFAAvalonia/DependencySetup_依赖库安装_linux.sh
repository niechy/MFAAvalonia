#!/bin/bash

# 启用颜色输出
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
WHITE='\033[1;37m'
BOLD='\033[1m'
NC='\033[0m' # 重置颜色

# 初始化错误标志和架构变量
error_occurred=0
arch="x64"

# 检测系统架构（x64/arm64）
detect_arch() {
    local uname_arch=$(uname -m)
    case $uname_arch in
        x86_64) arch="x64" ;;
        aarch64) arch="arm64" ;;
        *) 
            echo -e "${RED}不支持的架构: $uname_arch${NC}"
            exit 1 
            ;;
    esac
    echo -e "${BOLD}${BLUE}检测到系统架构: $arch${NC}"
}

# 检查并获取管理员权限
check_admin() {
    if [ "$EUID" -ne 0 ]; then
        echo -e "${YELLOW}需要管理员权限，即将请求 sudo 密码...${NC}"
        sudo "$0" "$@"
        exit $?
    fi
}

# 安装 .NET Runtime 10
install_dotnet() {
    echo -e "\n${BLUE}===================================================================================================="
    echo -e "${BOLD}${CYAN}正在安装 .NET Runtime 10 ($arch)${NC}"
    echo -e "${BOLD}${CYAN}Installing .NET Runtime 10 ($arch)${NC}"
    echo -e "${BLUE}===================================================================================================="${NC}

    # 添加 Microsoft 包源（适配 Debian/Ubuntu 系列，其他发行版需调整）
    echo -e "${YELLOW}添加 Microsoft 包源...${NC}"
    wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    dpkg -i packages-microsoft-prod.deb > /dev/null 2>&1
    rm -f packages-microsoft-prod.deb

    # 更新包列表并安装 .NET Runtime 10
    apt-get update > /dev/null 2>&1
    if [ "$arch" = "x64" ]; then
        apt-get install -y dotnet-runtime-10.0
    else
        apt-get install -y dotnet-runtime-10.0:arm64
    fi

    if [ $? -ne 0 ]; then
        error_occurred=1
        echo -e "${RED}.NET Runtime 10 安装失败${NC}"
    fi
}

# 输出手动下载链接
print_manual_links() {
    echo -e "\n${YELLOW}您可以手动下载以下组件安装：${NC}"
    echo -e "${YELLOW}You can manually download and install the following components:${NC}\n"

    echo -e "${WHITE}.NET Runtime 10 ($arch):${NC}"
    echo -e "${CYAN}https://aka.ms/dotnet/10.0/dotnet-runtime-10.0-linux-$arch.tar.gz${NC}"
}

# 主逻辑
main() {
    detect_arch
    check_admin
    install_dotnet

    # 输出结果
    echo -e "\n"
    if [ $error_occurred -eq 0 ]; then
        echo -e "${BOLD}${GREEN}依赖安装完成！建议重启后再运行应用。${NC}"
        echo -e "${BOLD}${GREEN}Dependencies installed successfully! Please restart your system before running the application.${NC}"
    else
        echo -e "${RED}===================================================================================================="
        echo -e "${BOLD}${RED}依赖安装过程中出现错误${NC}"
        echo -e "${BOLD}${RED}Errors occurred during dependency installation${NC}"
        echo -e "\n${YELLOW}若使用非 Debian/Ubuntu 系统，请手动安装或调整包管理器命令（如 yum/dnf）${NC}"
        echo -e "${YELLOW}For non-Debian/Ubuntu systems, please install manually or adjust package manager commands (e.g., yum/dnf)${NC}"
        print_manual_links
        echo -e "${RED}===================================================================================================="${NC}
    fi

    read -p "按 Enter 键退出..."
}

main