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

# 检测系统架构（x64/arm64，M系列芯片为arm64）
detect_arch() {
    local uname_arch=$(uname -m)
    case $uname_arch in
        x86_64) arch="x64" ;;
        arm64) arch="arm64" ;;
        *) 
            echo -e "${RED}不支持的架构: $uname_arch${NC}"
            exit 1 
            ;;
    esac
    echo -e "${BOLD}${BLUE}检测到系统架构: $arch${NC}"
}

# 检查并安装 Homebrew（macOS 包管理器）
check_brew() {
    if ! command -v brew &> /dev/null; then
        echo -e "${YELLOW}未检测到 Homebrew，正在安装...${NC}"
        /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
        # 配置 brew 环境变量（针对当前会话）
        eval "$(/opt/homebrew/bin/brew shellenv)"
    fi
}

# 安装 .NET Runtime 10
install_dotnet() {
    echo -e "\n${BLUE}===================================================================================================="
    echo -e "${BOLD}${CYAN}正在安装 .NET Runtime 10 ($arch)${NC}"
    echo -e "${BOLD}${CYAN}Installing .NET Runtime 10 ($arch)${NC}"
    echo -e "${BLUE}===================================================================================================="${NC}

    # 通过 Homebrew 安装 .NET Runtime 10（包名基于预测，正式发布后可能调整）
    if [ "$arch" = "x64" ]; then
        brew install --cask dotnet-runtime-10
    else
        brew install --cask dotnet-runtime-10-arm64
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
    echo -e "${CYAN}https://aka.ms/dotnet/10.0/dotnet-runtime-10.0-osx-$arch.pkg${NC}"
}

# 主逻辑
main() {
    detect_arch
    check_brew
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
        print_manual_links
        echo -e "${RED}===================================================================================================="${NC}
    fi

    read -p "按 Enter 键退出..."
}

main